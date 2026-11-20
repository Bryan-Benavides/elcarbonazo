using System.Globalization;
using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed class CajaCierreExecutor(AppDbContext context, IAuditRecorder audit, TimeProvider time) : ICajaEjecutorDominio
{
    public bool PuedeEjecutar(string tipo) => tipo == "CerrarTurno";

    public async Task<CajaResultadoDominio> EjecutarAsync(CajaEjecucionPreparada operacion, CancellationToken cancellationToken)
    {
        if (operacion.Tipo != "CerrarTurno" || operacion.Version != 1) throw new InvalidOperationException("Contrato no soportado.");
        using var json = JsonDocument.Parse(operacion.ArgumentosJson);
        var root = json.RootElement;
        if (!root.TryGetProperty("turnoId", out var shiftValue) || !shiftValue.TryGetInt32(out var shiftId) || shiftId <= 0 ||
            !root.TryGetProperty("efectivoContado", out var countedValue) || !countedValue.TryGetDecimal(out var counted) ||
            counted < 0 || counted > 9999999999.99m || decimal.Round(counted, 2) != counted ||
            !root.TryGetProperty("confirmado", out var confirmed) || confirmed.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("motivo", out var reasonValue) || reasonValue.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
            throw new InvalidOperationException("La operación preparada no contiene un cierre válido.");
        var reason = reasonValue.ValueKind == JsonValueKind.Null ? null : reasonValue.GetString();
        var shift = await context.TurnosCaja.FromSqlInterpolated($"SELECT * FROM turno_caja WHERE id_turno_caja = {shiftId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (shift is null || shift.Estado != CajaStates.TurnoAbierto) return Reject("TURNO_NO_ABIERTO");

        // MVCC read after the global barrier and shift lock: never lock another operation row here.
        var pending = await context.OperacionesCaja.AsNoTracking()
            .Where(x => x.IdOperacion != operacion.IdOperacion && x.Estado == CajaStates.OperacionPreparada)
            .Select(x => new { x.Tipo, x.Argumentos }).ToListAsync(cancellationToken);
        foreach (var item in pending)
        {
            if (item.Tipo == "AbrirTurno") return Reject("OPERACION_NO_TERMINAL");
            if (item.Tipo is not ("CobrarCuenta" or "CerrarTurno") || item.Argumentos is null) return Reject("OPERACION_NO_TERMINAL");
            try
            {
                using var arguments = JsonDocument.Parse(item.Argumentos);
                if (!arguments.RootElement.TryGetProperty("turnoId", out var target) || !target.TryGetInt32(out var targetId) || targetId <= 0 || targetId == shiftId)
                    return Reject("OPERACION_NO_TERMINAL");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return Reject("OPERACION_NO_TERMINAL"); }
        }
        var totals = await context.Cobros.AsNoTracking().Where(x => x.IdTurnoCaja == shiftId)
            .GroupBy(x => x.MetodoPago)
            .Select(g => new { Metodo = g.Key, Total = g.Sum(x => x.Total) }).ToListAsync(cancellationToken);
        var cash = totals.SingleOrDefault(x => x.Metodo == CajaStates.Efectivo)?.Total ?? 0m;
        var card = totals.SingleOrDefault(x => x.Metodo == CajaStates.Tarjeta)?.Total ?? 0m;
        var transfer = totals.SingleOrDefault(x => x.Metodo == CajaStates.Transferencia)?.Total ?? 0m;
        var expected = checked(shift.FondoInicial + cash);
        if (expected > 9999999999.99m || cash > 9999999999.99m || card > 9999999999.99m || transfer > 9999999999.99m)
            throw new InvalidOperationException("Los totales del turno exceden el rango persistible.");
        var difference = counted - expected;
        if ((difference != 0 && string.IsNullOrWhiteSpace(reason)) || (difference == 0 && reason is not null))
            return new(false, 400, "VALIDATION_ERROR", "{\"estado\":\"Rechazada\",\"codigo\":\"VALIDATION_ERROR\"}");
        var pendingAccounts = await context.Pedidos.AsNoTracking().CountAsync(x => PedidoStates.Active.Contains(x.Estado), cancellationToken);
        var now = time.GetUtcNow();
        shift.Estado = CajaStates.TurnoCerrado;
        shift.FechaCierre = now;
        shift.IdUsuarioCierre = operacion.IdUsuario;
        shift.IdOperacionCierre = operacion.IdOperacion;
        shift.EfectivoEsperado = expected;
        shift.EfectivoContado = counted;
        shift.Diferencia = difference;
        shift.TotalEfectivo = cash;
        shift.TotalTarjeta = card;
        shift.TotalTransferencia = transfer;
        shift.CantidadCuentasPendientes = pendingAccounts;
        shift.MotivoDiferencia = reason;
        audit.Record("TurnoCajaCerrado", "Exitoso", operacion.IdUsuario, entityType: "TurnoCaja",
            entityId: shiftId.ToString(CultureInfo.InvariantCulture), details: new Dictionary<string, object?>
            { ["efectivoEsperado"] = expected, ["efectivoContado"] = counted, ["diferencia"] = difference, ["idOperacion"] = operacion.IdOperacion });
        var response = JsonSerializer.Serialize(new
        {
            estado = "Completada",
            codigo = "TURNO_CERRADO",
            idTurnoCaja = shiftId,
            efectivoEsperado = expected,
            efectivoContado = counted,
            diferencia = difference,
            totalEfectivo = cash,
            totalTarjeta = card,
            totalTransferencia = transfer,
            cantidadCuentasPendientes = pendingAccounts,
            fechaCierre = now
        });
        return new(true, 200, "TURNO_CERRADO", response);
    }

    private static CajaResultadoDominio Reject(string code) =>
        new(false, 409, code, JsonSerializer.Serialize(new { estado = "Rechazada", codigo = code }));
}
