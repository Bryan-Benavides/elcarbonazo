using System.Data;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed class CajaTurnosService(AppDbContext context)
{
    public async Task<CajaTurnoResumenDto?> ActualAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var shift = await context.TurnosCaja.AsNoTracking().SingleOrDefaultAsync(x => x.Estado == CajaStates.TurnoAbierto, cancellationToken);
        var result = shift is null ? null : await ResumenAsync(shift, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<CajaTurnoResumenDto?> PorIdAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return null;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var shift = await context.TurnosCaja.AsNoTracking().SingleOrDefaultAsync(x => x.IdTurnoCaja == id, cancellationToken);
        var result = shift is null ? null : await ResumenAsync(shift, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<CajaTurnoResumenDto> ResumenAsync(TurnoCaja shift, CancellationToken cancellationToken)
    {
        decimal cash;
        decimal card;
        decimal transfer;
        decimal expected;
        int pending;
        if (shift.Estado == CajaStates.TurnoCerrado)
        {
            cash = shift.TotalEfectivo ?? throw new InvalidOperationException("Cierre sin totales.");
            card = shift.TotalTarjeta ?? throw new InvalidOperationException("Cierre sin totales.");
            transfer = shift.TotalTransferencia ?? throw new InvalidOperationException("Cierre sin totales.");
            expected = shift.EfectivoEsperado ?? throw new InvalidOperationException("Cierre sin efectivo esperado.");
            pending = shift.CantidadCuentasPendientes ?? throw new InvalidOperationException("Cierre sin cuentas pendientes.");
        }
        else
        {
            var totals = await context.Cobros.AsNoTracking().Where(x => x.IdTurnoCaja == shift.IdTurnoCaja)
                .GroupBy(x => x.MetodoPago).Select(x => new { Metodo = x.Key, Total = x.Sum(y => y.Total) }).ToListAsync(cancellationToken);
            cash = totals.SingleOrDefault(x => x.Metodo == CajaStates.Efectivo)?.Total ?? 0m;
            card = totals.SingleOrDefault(x => x.Metodo == CajaStates.Tarjeta)?.Total ?? 0m;
            transfer = totals.SingleOrDefault(x => x.Metodo == CajaStates.Transferencia)?.Total ?? 0m;
            expected = checked(shift.FondoInicial + cash);
            pending = await context.Pedidos.AsNoTracking().CountAsync(x => PedidoStates.Active.Contains(x.Estado), cancellationToken);
        }
        return new CajaTurnoResumenDto(shift.IdTurnoCaja, shift.Estado, shift.FechaApertura, shift.IdUsuarioApertura,
            shift.FondoInicial, cash, card, transfer, expected, shift.EfectivoContado, shift.Diferencia, pending,
            shift.FechaCierre, shift.IdUsuarioCierre, shift.MotivoDiferencia);
    }
}
