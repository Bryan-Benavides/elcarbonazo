using System.Data;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed class CuentasService(AppDbContext context) : ICuentasService
{
    public async Task<CuentaActivaDto?> GetActivaAsync(
        int? mesaId, int? grupoId, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);

        var exists = mesaId is not null
            ? await context.Mesas.AsNoTracking().AnyAsync(mesa => mesa.IdMesa == mesaId, cancellationToken)
            : await context.GruposMesa.AsNoTracking().AnyAsync(
                grupo => grupo.IdGrupo == grupoId && grupo.Activo, cancellationToken);
        if (!exists)
        {
            return null;
        }

        var pedido = await context.Pedidos.AsNoTracking()
            .Include(item => item.Comandas).ThenInclude(comanda => comanda.Detalles)
            .SingleOrDefaultAsync(item =>
                (mesaId != null ? item.IdMesa == mesaId : item.IdGrupo == grupoId) &&
                PedidoStates.Active.Contains(item.Estado), cancellationToken);
        if (pedido is null)
        {
            return new CuentaActivaDto(null);
        }

        var commands = pedido.Comandas.OrderBy(item => item.FechaHora).ThenBy(item => item.IdComanda)
            .Select(item => new CuentaComandaDto(
                item.IdComanda, item.IdUsuario, item.FechaHora, item.Estado, item.Version,
                item.Detalles.OrderBy(detail => detail.IdDetalle).Select(detail => new CuentaDetalleDto(
                    detail.IdDetalle, detail.IdProducto, detail.NombreProducto, detail.Cantidad,
                    detail.PrecioUnitario, detail.Subtotal, detail.Notas)).ToArray())).ToArray();
        var legacy = commands.Length == 0;
        return new CuentaActivaDto(new CuentaDto(
            pedido.IdPedido, pedido.IdMesa, pedido.IdGrupo, pedido.IdUsuario, pedido.FechaHora,
            pedido.Subtotal, pedido.Impuestos, pedido.Total, pedido.Estado,
            legacy, legacy ? "Cuenta anterior sin detalle disponible." : null, commands));
    }
}
