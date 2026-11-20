using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed class MesasService(AppDbContext context, IAuditRecorder auditRecorder, TimeProvider? time = null) : IMesasService
{
    public async Task<IReadOnlyList<MesaDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var now = (time ?? TimeProvider.System).GetUtcNow();
        return await context.Mesas
            .AsNoTracking()
            .OrderBy(mesa => mesa.Numero)
            .Select(mesa => new MesaDto(
                mesa.IdMesa,
                mesa.IdGrupo,
                mesa.Numero,
                mesa.Capacidad,
                mesa.Estado,
                mesa.Version,
                mesa.Reservaciones
                    .Where(reserva => reserva.Estado == "Activa" && reserva.InicioUtc >= now)
                    .OrderBy(reserva => reserva.InicioUtc)
                    .Select(reserva => new ProximaReservacionDto(
                        reserva.InicioUtc, reserva.FinUtc, reserva.CantidadPersonas))
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public async Task<UpdateMesaEstadoResult> UpdateEstadoAsync(
        int id,
        UpdateMesaEstadoRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Estado is null || !MesaStates.All.Contains(request.Estado))
        {
            return new UpdateMesaEstadoResult(
                UpdateMesaEstadoOutcome.InvalidState,
                Message: "Estado inválido. Use Libre, Ocupada, Reservada o Agrupada.");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var mesa = await context.Mesas
            .FromSqlInterpolated($"""
                SELECT xmin, * FROM mesa WHERE id_mesa = {id} FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (mesa is null)
        {
            return new UpdateMesaEstadoResult(UpdateMesaEstadoOutcome.NotFound);
        }

        if (request.Version is null || mesa.Version != request.Version.Value)
        {
            return new UpdateMesaEstadoResult(UpdateMesaEstadoOutcome.ConcurrencyConflict);
        }

        if (request.Estado == mesa.Estado)
        {
            return new UpdateMesaEstadoResult(UpdateMesaEstadoOutcome.Success, ToDto(mesa));
        }

        if (mesa.Estado == MesaStates.Agrupada)
        {
            return new UpdateMesaEstadoResult(
                UpdateMesaEstadoOutcome.GroupedConflict,
                Message: "Las mesas agrupadas solo pueden modificarse mediante operaciones de grupo.");
        }

        if (request.Estado == MesaStates.Agrupada)
        {
            return new UpdateMesaEstadoResult(
                UpdateMesaEstadoOutcome.InvalidTransition,
                Message: "El estado Agrupada solo puede asignarse mediante operaciones de grupo.");
        }

        if (!MesaStateTransitions.IsAllowed(mesa.Estado, request.Estado))
        {
            return new UpdateMesaEstadoResult(
                UpdateMesaEstadoOutcome.InvalidTransition,
                Message: $"No se permite la transición de {mesa.Estado} a {request.Estado}.");
        }

        if (request.Estado == MesaStates.Libre &&
            await HasPendingOrderAsync(id, cancellationToken))
        {
            return new UpdateMesaEstadoResult(
                UpdateMesaEstadoOutcome.PendingOrder,
                Message: "No se puede liberar una mesa con una cuenta pendiente.");
        }

        var previousState = mesa.Estado;
        mesa.Estado = request.Estado;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new UpdateMesaEstadoResult(UpdateMesaEstadoOutcome.ConcurrencyConflict);
        }

        auditRecorder.Record(
            "MesaEstadoCambiado",
            "Exitoso",
            entityType: "mesa",
            entityId: mesa.IdMesa.ToString(),
            details: new Dictionary<string, object?>
            {
                ["estadoAnterior"] = previousState,
                ["estadoNuevo"] = request.Estado
            });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new UpdateMesaEstadoResult(UpdateMesaEstadoOutcome.Success, ToDto(mesa));
    }

    private async Task<bool> HasPendingOrderAsync(int id, CancellationToken cancellationToken)
    {
        return await context.Pedidos.AnyAsync(
            pedido => pedido.IdMesa == id && PedidoStates.Active.Contains(pedido.Estado),
            cancellationToken);
    }

    private static MesaDto ToDto(Mesa mesa) => new(
        mesa.IdMesa,
        mesa.IdGrupo,
        mesa.Numero,
        mesa.Capacidad,
        mesa.Estado,
        mesa.Version);
}
