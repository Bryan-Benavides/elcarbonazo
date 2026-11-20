using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

internal static class CajaCuentaLock
{
    public static async Task<Pedido?> ObtenerAsync(AppDbContext context, int id, CancellationToken cancellationToken)
    {
        var destination = await context.Pedidos.AsNoTracking().Where(x => x.IdPedido == id)
            .Select(x => new { x.IdMesa, x.IdGrupo }).SingleOrDefaultAsync(cancellationToken);
        if (destination is null) return null;
        if (destination.IdGrupo is int groupId)
        {
            var group = await context.GruposMesa.FromSqlInterpolated($"SELECT * FROM grupo_mesa WHERE id_grupo = {groupId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (group is null || !group.Activo) return null;
            var members = await context.Mesas.FromSqlInterpolated($"SELECT xmin, * FROM mesa WHERE id_grupo = {groupId} ORDER BY id_mesa FOR UPDATE")
                .ToListAsync(cancellationToken);
            if (members.Count < 2 || members.Any(x => x.Estado != MesaStates.Agrupada)) return null;
        }
        else if (destination.IdMesa is int tableId)
        {
            var table = await context.Mesas.FromSqlInterpolated($"SELECT xmin, * FROM mesa WHERE id_mesa = {tableId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (table is null || table.Estado != MesaStates.Ocupada || table.IdGrupo is not null) return null;
        }
        else return null;
        var order = await context.Pedidos.FromSqlInterpolated($"SELECT * FROM pedido WHERE id_pedido = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (order is null || order.IdMesa != destination.IdMesa || order.IdGrupo != destination.IdGrupo) return null;
        var commands = await context.Comandas.FromSqlInterpolated($"SELECT xmin, * FROM comanda WHERE id_pedido = {id} ORDER BY id_comanda FOR UPDATE")
            .ToListAsync(cancellationToken);
        var ids = commands.Select(x => x.IdComanda).ToArray();
        await context.DetallesPedido.Where(x => ids.Contains(x.IdComanda)).LoadAsync(cancellationToken);
        return order;
    }
}
