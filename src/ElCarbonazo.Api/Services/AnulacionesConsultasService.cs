using System.Security.Cryptography;
using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed class AnulacionesConsultasService(AppDbContext context, TimeProvider time)
{
    private sealed record Cursor(int Version, string Filtro, long Id);

    public async Task<(int Status, string Code, AuditoriaPagina<AnulacionAuditoriaDto>? Page)> AnulacionesAsync(
        DateTimeOffset? desde, DateTimeOffset? hasta, string? cursor, int limite, CancellationToken ct,
        int? actorId = null, Guid? sessionId = null)
    {
        if (!ValidRange(desde, hasta) || limite is < 1 or > 100)
            return (400, "VALIDATION_ERROR", null);
        if ((actorId.HasValue || sessionId.HasValue) &&
            (actorId is null || sessionId is null || !await AuditorAutorizadoAsync(actorId.Value, sessionId.Value, ct)))
            return (401, "AUTH_SESION_EXPIRADA", null);
        var filter = FilterHash("anulaciones-v1", desde, hasta, null);
        var last = ParseCursor(cursor, filter);
        if (last == -1) return (400, "VALIDATION_ERROR", null);
        var query = from cancellation in context.AnulacionesPedido.AsNoTracking()
                    join order in context.Pedidos.AsNoTracking() on cancellation.IdPedido equals order.IdPedido
                    where (!desde.HasValue || cancellation.FechaUtc >= desde.Value) &&
                          (!hasta.HasValue || cancellation.FechaUtc < hasta.Value)
                    select new { Cancellation = cancellation, Order = order };
        if (last is long id) query = query.Where(x => x.Cancellation.IdAnulacion < id);
        var rows = await query.OrderByDescending(x => x.Cancellation.IdAnulacion).Take(limite + 1).ToListAsync(ct);
        var more = rows.Count > limite;
        if (more) rows.RemoveAt(rows.Count - 1);
        var items = rows.Select(x => new AnulacionAuditoriaDto(
            x.Cancellation.IdAnulacion, x.Cancellation.IdPedido, x.Cancellation.IdComanda,
            x.Cancellation.IdDetalle, x.Order.IdMesa, x.Order.IdGrupo, x.Cancellation.CantidadAnulada,
            x.Cancellation.MontoRevertido, x.Cancellation.IdUsuarioSolicita, x.Cancellation.IdUsuarioAutoriza,
            x.Cancellation.Motivo, x.Cancellation.InventarioRevertido, x.Cancellation.FechaUtc)).ToArray();
        return (200, "OK", new(items, more ? MakeCursor(filter, items[^1].IdAnulacion) : null));
    }

    public async Task<(int Status, string Code, AuditoriaPagina<EventoAuditoriaDto>? Page)> EventosAsync(
        DateTimeOffset? desde, DateTimeOffset? hasta, string? tipo, string? cursor, int limite, CancellationToken ct,
        int? actorId = null, Guid? sessionId = null)
    {
        if (!ValidRange(desde, hasta) || limite is < 1 or > 100 ||
            (tipo is not null && (tipo.Length is < 1 or > 80 || tipo.Any(char.IsControl))))
            return (400, "VALIDATION_ERROR", null);
        if ((actorId.HasValue || sessionId.HasValue) &&
            (actorId is null || sessionId is null || !await AuditorAutorizadoAsync(actorId.Value, sessionId.Value, ct)))
            return (401, "AUTH_SESION_EXPIRADA", null);
        var filter = FilterHash("eventos-v1", desde, hasta, tipo);
        var last = ParseCursor(cursor, filter);
        if (last == -1) return (400, "VALIDATION_ERROR", null);
        var query = context.EventosAuditoria.AsNoTracking()
            .Where(x => (!desde.HasValue || x.FechaHora >= desde.Value) &&
                        (!hasta.HasValue || x.FechaHora < hasta.Value) &&
                        (tipo == null || x.TipoEvento == tipo));
        if (last is long id) query = query.Where(x => x.IdEvento < id);
        var rows = await query.OrderByDescending(x => x.IdEvento).Take(limite + 1)
            .Select(x => new EventoAuditoriaDto(x.IdEvento, x.FechaHora, x.TipoEvento, x.Resultado,
                x.IdUsuario, x.TipoEntidad, x.IdEntidad)).ToListAsync(ct);
        var more = rows.Count > limite;
        if (more) rows.RemoveAt(rows.Count - 1);
        return (200, "OK", new(rows, more ? MakeCursor(filter, rows[^1].IdEvento) : null));
    }

    private async Task<bool> AuditorAutorizadoAsync(int actorId, Guid sessionId, CancellationToken ct) =>
        await CajaSesion.VigenteAsync(context, sessionId, actorId, time.GetUtcNow(), ct) &&
        await context.Usuarios.AsNoTracking().AnyAsync(x => x.IdUsuario == actorId && x.Activo &&
            (x.Rol == UserRoles.Jefa || x.Rol == UserRoles.Supervisor), ct);

    private static bool ValidRange(DateTimeOffset? from, DateTimeOffset? until) =>
        (!from.HasValue || from.Value.Offset == TimeSpan.Zero) &&
        (!until.HasValue || until.Value.Offset == TimeSpan.Zero) &&
        (!from.HasValue || !until.HasValue || from.Value < until.Value);

    private static string FilterHash(string kind, DateTimeOffset? from, DateTimeOffset? until, string? type)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { kind, from, until, type });
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static long? ParseCursor(string? cursor, string filter)
    {
        if (cursor is null) return null;
        try
        {
            if (cursor.Length is 0 or > 512 || cursor.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
                return -1;
            var encoded = cursor.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            var parsed = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(encoded));
            return parsed is { Version: 1, Id: > 0 } && parsed.Filtro == filter ? parsed.Id : -1;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return -1;
        }
    }

    private static string MakeCursor(string filter, long id) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Cursor(1, filter, id)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
