using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed class GruposMesaService(
    AppDbContext context,
    TimeProvider timeProvider,
    IAuditRecorder auditRecorder) : IGruposMesaService
{
    public async Task<GruposMesaResult> CreateAsync(
        CreateGrupoMesaRequest request,
        int userId,
        CancellationToken cancellationToken)
    {
        var requestedTables = request.Mesas;
        if (requestedTables is null || requestedTables.Count is < 2 or > 15 ||
            requestedTables.Select(table => table.IdMesa).Distinct().Count() != requestedTables.Count)
        {
            return new GruposMesaResult(
                GrupoMesaOutcome.InvalidRequest,
                Message: "El grupo requiere entre 2 y 15 mesas distintas.");
        }

        var requestedById = requestedTables.ToDictionary(table => table.IdMesa);
        var ids = requestedById.Keys.Order().ToArray();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var tables = await context.Mesas
            .FromSqlInterpolated($"""
                SELECT xmin, *
                FROM mesa
                WHERE id_mesa = ANY({ids})
                ORDER BY id_mesa
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (tables.Count != ids.Length)
        {
            return new GruposMesaResult(
                GrupoMesaOutcome.NotEligible,
                Message: "Una o más mesas solicitadas no existen.");
        }

        if (tables.Any(table => table.Version != requestedById[table.IdMesa].Version))
        {
            return new GruposMesaResult(
                GrupoMesaOutcome.ConcurrencyConflict,
                Message: "Una o más mesas cambiaron desde la última consulta.");
        }

        var pendingTableIds = await context.Pedidos
            .Where(pedido => ids.Contains(pedido.IdMesa!.Value) && PedidoStates.Active.Contains(pedido.Estado))
            .Select(pedido => pedido.IdMesa!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (tables.Any(table =>
                table.Estado != MesaStates.Libre ||
                table.IdGrupo is not null ||
                pendingTableIds.Contains(table.IdMesa)))
        {
            return new GruposMesaResult(
                GrupoMesaOutcome.NotEligible,
                Message: "Solo se pueden agrupar mesas libres, sin grupo y sin pedidos pendientes.");
        }

        var now = timeProvider.GetUtcNow();
        var group = new GrupoMesa
        {
            IdUsuarioCreacion = userId,
            FechaCreacion = now,
            Activo = true
        };
        context.GruposMesa.Add(group);
        await context.SaveChangesAsync(cancellationToken);

        foreach (var table in tables)
        {
            table.IdGrupo = group.IdGrupo;
            table.Estado = MesaStates.Agrupada;
            table.PermitePedidoTrasAnulacion = false;
            group.Miembros.Add(new GrupoMesaMiembro
            {
                IdGrupo = group.IdGrupo,
                IdMesa = table.IdMesa,
                FechaAsignacion = now
            });
        }

        auditRecorder.Record(
            "GrupoMesaCreado",
            "Exitoso",
            userId,
            entityType: "grupo_mesa",
            entityId: group.IdGrupo.ToString(),
            details: new Dictionary<string, object?>
            {
                ["mesas"] = tables.Select(table => table.IdMesa).Order().ToArray()
            });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new GruposMesaResult(GrupoMesaOutcome.Success, ToDto(group, tables));
    }

    public async Task<GruposMesaResult> CloseAsync(
        int groupId,
        int userId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var group = await context.GruposMesa
            .FromSqlInterpolated($"""
                SELECT *
                FROM grupo_mesa
                WHERE id_grupo = {groupId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (group is null)
        {
            return new GruposMesaResult(GrupoMesaOutcome.NotFound);
        }

        if (!group.Activo)
        {
            return new GruposMesaResult(GrupoMesaOutcome.Success);
        }

        var tables = await context.Mesas
            .FromSqlInterpolated($"""
                SELECT xmin, *
                FROM mesa
                WHERE id_grupo = {groupId}
                ORDER BY id_mesa
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (await context.Pedidos.AnyAsync(
                pedido => pedido.IdGrupo == groupId && PedidoStates.Active.Contains(pedido.Estado),
                cancellationToken))
        {
            return new GruposMesaResult(
                GrupoMesaOutcome.PendingOrders,
                Message: "No se puede cerrar un grupo con pedidos pendientes.");
        }

        var now = timeProvider.GetUtcNow();
        group.Activo = false;
        group.PermitePedidoTrasAnulacion = false;
        group.FechaCierre = now;
        group.IdUsuarioCierre = userId;
        foreach (var table in tables)
        {
            table.IdGrupo = null;
            table.Estado = MesaStates.Libre;
            table.PermitePedidoTrasAnulacion = false;
        }

        auditRecorder.Record(
            "GrupoMesaCerrado",
            "Exitoso",
            userId,
            entityType: "grupo_mesa",
            entityId: group.IdGrupo.ToString(),
            details: new Dictionary<string, object?>
            {
                ["mesas"] = tables.Select(table => table.IdMesa).Order().ToArray()
            });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new GruposMesaResult(GrupoMesaOutcome.Success);
    }

    private static GrupoMesaDto ToDto(GrupoMesa group, IReadOnlyList<Mesa> tables) => new(
        group.IdGrupo,
        group.IdUsuarioCreacion,
        group.FechaCreacion,
        group.Activo,
        tables.OrderBy(table => table.Numero).Select(table => new MesaDto(
            table.IdMesa,
            table.IdGrupo,
            table.Numero,
            table.Capacidad,
            table.Estado,
            table.Version)).ToArray());
}
