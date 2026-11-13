using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed class CajaAperturaExecutor(AppDbContext context, IAuditRecorder audit, TimeProvider time) : ICajaEjecutorDominio
{
    public bool PuedeEjecutar(string tipo) => tipo == "AbrirTurno";

    public async Task<CajaResultadoDominio> EjecutarAsync(CajaEjecucionPreparada operacion, CancellationToken cancellationToken)
    {
        if (operacion.Tipo != "AbrirTurno" || operacion.Version != 1) throw new InvalidOperationException("Contrato no soportado.");
        using var data = JsonDocument.Parse(operacion.ArgumentosJson);
        var root = data.RootElement;
        if (!root.TryGetProperty("fondoInicial", out var fundElement) || !fundElement.TryGetDecimal(out var fund) ||
            !root.TryGetProperty("confirmado", out var confirmed) || confirmed.ValueKind != JsonValueKind.True ||
            fund < 0 || fund > 9999999999.99m || decimal.Round(fund, 2) != fund)
            throw new InvalidOperationException("La operación preparada no contiene una apertura válida.");

        await CajaLocks.CajaCompartidaAsync(context, cancellationToken);
        if (await context.OperacionesCaja.AsNoTracking().AnyAsync(x => x.IdOperacion != operacion.IdOperacion &&
                x.Estado == CajaStates.OperacionPreparada, cancellationToken))
            return new(false, 409, "OPERACION_NO_TERMINAL", "{\"estado\":\"Rechazada\",\"codigo\":\"OPERACION_NO_TERMINAL\"}");
        if (await context.TurnosCaja.AsNoTracking().AnyAsync(x => x.Estado == CajaStates.TurnoAbierto, cancellationToken))
            return new(false, 409, "TURNO_YA_ABIERTO", "{\"estado\":\"Rechazada\",\"codigo\":\"TURNO_YA_ABIERTO\"}");

        var shift = new TurnoCaja
        {
            Estado = CajaStates.TurnoAbierto,
            FechaApertura = time.GetUtcNow(),
            IdUsuarioApertura = operacion.IdUsuario,
            FondoInicial = fund,
            IdOperacionApertura = operacion.IdOperacion
        };
        context.TurnosCaja.Add(shift);
        await context.SaveChangesAsync(cancellationToken);
        audit.Record("TurnoCajaAbierto", "Exitoso", operacion.IdUsuario, entityType: "TurnoCaja", entityId: shift.IdTurnoCaja.ToString(System.Globalization.CultureInfo.InvariantCulture),
            details: new Dictionary<string, object?> { ["fondoInicial"] = fund, ["idOperacion"] = operacion.IdOperacion });
        var response = JsonSerializer.Serialize(new
        {
            estado = "Completada",
            codigo = "TURNO_ABIERTO",
            idTurnoCaja = shift.IdTurnoCaja,
            fondoInicial = fund,
            fechaApertura = shift.FechaApertura,
            idUsuario = operacion.IdUsuario
        });
        return new(true, 201, "TURNO_ABIERTO", response);
    }
}
