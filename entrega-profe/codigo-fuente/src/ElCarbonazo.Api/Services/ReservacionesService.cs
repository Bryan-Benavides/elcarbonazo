using System.Text;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed class ReservacionesService(AppDbContext context, IAuditRecorder audit, TimeProvider time)
{
    private static readonly TimeSpan MaxDuration = TimeSpan.FromHours(8);
    private static readonly TimeSpan MaxAdvance = TimeSpan.FromDays(365);

    public async Task<ReservacionesError> DisponibilidadAsync(
        DateTimeOffset desde, DateTimeOffset hasta, int personas, CancellationToken ct)
    {
        var validation = ValidarIntervalo(desde, hasta, time.GetUtcNow());
        if (validation is not null || personas is < 1 or > 20)
            return new(400, "VALIDATION_ERROR", validation ?? "La cantidad de personas debe estar entre 1 y 20.");
        var disponibles = await context.Mesas.AsNoTracking()
            .Where(mesa => mesa.Estado == "Libre" && mesa.IdGrupo == null && mesa.Capacidad >= personas &&
                !mesa.Reservaciones.Any(reserva => reserva.Estado == "Activa" &&
                    reserva.InicioUtc < hasta && reserva.FinUtc > desde))
            .OrderBy(mesa => mesa.Numero)
            .Select(mesa => new DisponibilidadMesaDto(mesa.IdMesa, mesa.Numero, mesa.Capacidad))
            .ToListAsync(ct);
        return new(200, "OK", Disponibles: disponibles);
    }

    public async Task<(int Status, string Code, string? Message, IReadOnlyList<ReservacionDto>? Items)> ListarAsync(
        DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
    {
        if (!RangoConsultaValido(desde, hasta)) return (400, "VALIDATION_ERROR", null, null);
        var items = await context.Reservaciones.AsNoTracking()
            .Where(x => x.Estado == "Activa" && x.InicioUtc < hasta && x.FinUtc > desde)
            .OrderBy(x => x.InicioUtc).ThenBy(x => x.IdReservacion)
            .Take(500)
            .Select(x => new ReservacionDto(x.IdReservacion, x.IdMesa, x.Mesa.Numero,
                x.NombreReferencia, x.CantidadPersonas, x.InicioUtc, x.FinUtc, x.Estado, x.CreadaUtc))
            .ToListAsync(ct);
        return (200, "OK", null, items);
    }

    public async Task<ReservacionesError> CrearAsync(CrearReservacionRequest? request, int actor, CancellationToken ct)
    {
        if (request is null || actor <= 0 || request.IdMesa <= 0 || request.CantidadPersonas is < 1 or > 20)
            return new(400, "VALIDATION_ERROR");
        var name = NormalizarReferencia(request.NombreReferencia);
        var validation = ValidarIntervalo(request.InicioUtc, request.FinUtc, time.GetUtcNow());
        if (name is null || validation is not null) return new(400, "VALIDATION_ERROR", validation ?? "La referencia es inválida.");

        await using var tx = await context.Database.BeginTransactionAsync(ct);
        var mesa = await context.Mesas.FromSqlInterpolated($"""
            SELECT xmin, * FROM mesa WHERE id_mesa = {request.IdMesa} FOR UPDATE
            """).SingleOrDefaultAsync(ct);
        if (mesa is null) return new(404, "MESA_NO_ENCONTRADA");
        if (mesa.Estado != "Libre" || mesa.IdGrupo is not null || request.CantidadPersonas > mesa.Capacidad)
            return new(409, "MESA_NO_DISPONIBLE");
        var conflict = await context.Reservaciones.AnyAsync(x => x.IdMesa == request.IdMesa &&
            x.Estado == "Activa" && x.InicioUtc < request.FinUtc && x.FinUtc > request.InicioUtc, ct);
        if (conflict) return new(409, "RESERVACION_CONFLICTO");
        var created = time.GetUtcNow();
        var reservation = new Reservacion
        {
            IdMesa = mesa.IdMesa,
            NombreReferencia = name,
            CantidadPersonas = (short)request.CantidadPersonas,
            InicioUtc = request.InicioUtc,
            FinUtc = request.FinUtc,
            Estado = "Activa",
            CreadaUtc = created
        };
        context.Reservaciones.Add(reservation);
        await context.SaveChangesAsync(ct);
        audit.Record("ReservacionCreada", "Exitoso", actor, entityType: "reservacion",
            entityId: reservation.IdReservacion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            details: new Dictionary<string, object?>
            {
                ["idMesa"] = mesa.IdMesa,
                ["cantidadPersonas"] = reservation.CantidadPersonas,
                ["inicioUtc"] = reservation.InicioUtc,
                ["finUtc"] = reservation.FinUtc
            });
        await context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(201, "OK", Reservacion: ToDto(reservation, mesa.Numero));
    }

    public async Task<ReservacionesError> CancelarAsync(int id, int actor, CancellationToken ct)
    {
        if (id <= 0 || actor <= 0) return new(400, "VALIDATION_ERROR");
        var mesaId = await context.Reservaciones.AsNoTracking()
            .Where(x => x.IdReservacion == id).Select(x => (int?)x.IdMesa).SingleOrDefaultAsync(ct);
        if (mesaId is null) return new(404, "RECURSO_NO_ENCONTRADO");
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        await context.Mesas.FromSqlInterpolated($"""
            SELECT xmin, * FROM mesa WHERE id_mesa = {mesaId.Value} FOR UPDATE
            """).SingleAsync(ct);
        var reservation = await context.Reservaciones.FromSqlInterpolated($"""
            SELECT * FROM reservacion WHERE id_reservacion = {id} FOR UPDATE
            """).Include(x => x.Mesa).SingleOrDefaultAsync(ct);
        if (reservation is null) return new(404, "RECURSO_NO_ENCONTRADO");
        if (reservation.Estado == "Cancelada")
            return new(200, "OK", Reservacion: ToDto(reservation, reservation.Mesa.Numero));
        reservation.Estado = "Cancelada";
        reservation.CanceladaUtc = time.GetUtcNow();
        await context.SaveChangesAsync(ct);
        audit.Record("ReservacionCancelada", "Exitoso", actor, entityType: "reservacion",
            entityId: id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            details: new Dictionary<string, object?> { ["idMesa"] = reservation.IdMesa });
        await context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(200, "OK", Reservacion: ToDto(reservation, reservation.Mesa.Numero));
    }

    private static string? NormalizarReferencia(string? value)
    {
        if (value is null) return null;
        try { value = value.Normalize(NormalizationForm.FormC).Trim(); }
        catch (ArgumentException) { return null; }
        return value.EnumerateRunes().Count() is >= 1 and <= 60 && !value.EnumerateRunes().Any(Rune.IsControl)
            ? value : null;
    }

    private static string? ValidarIntervalo(DateTimeOffset from, DateTimeOffset until, DateTimeOffset now)
    {
        if (from.Offset != TimeSpan.Zero || until.Offset != TimeSpan.Zero)
            return "Las fechas deben estar en UTC.";
        if (from <= now || from > now.Add(MaxAdvance) || until <= from || until - from > MaxDuration)
            return "El intervalo debe iniciar dentro de los próximos 365 días y durar entre 0 y 8 horas.";
        return null;
    }

    private static bool RangoConsultaValido(DateTimeOffset from, DateTimeOffset until) =>
        from.Offset == TimeSpan.Zero && until.Offset == TimeSpan.Zero &&
        from < until && until - from <= TimeSpan.FromDays(366);

    private static ReservacionDto ToDto(Reservacion entity, int tableNumber) => new(
        entity.IdReservacion, entity.IdMesa, tableNumber, entity.NombreReferencia, entity.CantidadPersonas,
        entity.InicioUtc, entity.FinUtc, entity.Estado, entity.CreadaUtc);
}
