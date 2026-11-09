using System.Text;
using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed record CocinaResult(int Status, string? Code = null, CocinaPaginaDto? Pagina = null, CocinaComandaDto? Comanda = null);

public sealed class CocinaService(AppDbContext context, IAuditRecorder audit)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CocinaResult> ColaAsync(string? cursor, int limite, CancellationToken cancellationToken)
    {
        if (limite is < 1 or > 100) return new CocinaResult(400, "VALIDATION_ERROR");
        Cursor? last = null;
        if (cursor is not null)
        {
            try
            {
                if (cursor.Length is 0 or > 256 || cursor.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
                    return new CocinaResult(400, "VALIDATION_ERROR");
                var encoded = cursor.Replace('-', '+').Replace('_', '/');
                encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
                last = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(encoded), JsonOptions);
                if (last is null || last.Id <= 0 || last.Fecha.Offset != TimeSpan.Zero || last.Fecha == default || last.Tipo != "cola-v1")
                    return new CocinaResult(400, "VALIDATION_ERROR");
            }
            catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
            {
                return new CocinaResult(400, "VALIDATION_ERROR");
            }
        }

        var query = context.Comandas.AsNoTracking()
            .Where(item => (item.Estado == ComandaStates.Abierta || item.Estado == ComandaStates.EnPreparacion) &&
                PedidoStates.Active.Contains(item.Pedido.Estado));
        if (last is not null)
        {
            var fecha = last.Fecha;
            var id = last.Id;
            query = query.Where(item => item.FechaHora > fecha || (item.FechaHora == fecha && item.IdComanda > id));
        }
        var items = await query.OrderBy(item => item.FechaHora).ThenBy(item => item.IdComanda)
            .Take(limite + 1).Include(item => item.Pedido).ThenInclude(pedido => pedido.Mesa)
            .Include(item => item.Detalles).ToListAsync(cancellationToken);
        var more = items.Count > limite;
        if (more) items.RemoveAt(items.Count - 1);
        var next = more ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                new Cursor("cola-v1", items[^1].FechaHora, items[^1].IdComanda), JsonOptions))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_') : null;
        return new CocinaResult(200, Pagina: new CocinaPaginaDto(items.Select(ToDto).ToArray(), next));
    }

    public async Task<CocinaResult> CambiarEstadoAsync(int id, CambiarComandaEstadoRequest request, int userId, CancellationToken cancellationToken)
    {
        if (id <= 0 || request.VersionComanda is null ||
            request.Estado is not (ComandaStates.EnPreparacion or ComandaStates.Lista))
            return new CocinaResult(400, "VALIDATION_ERROR");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var destination = await context.Comandas.AsNoTracking().Where(item => item.IdComanda == id)
            .Select(item => new { item.IdPedido, item.Pedido.IdMesa, item.Pedido.IdGrupo })
            .SingleOrDefaultAsync(cancellationToken);
        if (destination is null) return new CocinaResult(404, "HTTP_NO_ENCONTRADO");

        if (destination.IdGrupo is int groupId)
        {
            var group = await context.GruposMesa.FromSqlInterpolated($"""
                SELECT * FROM grupo_mesa WHERE id_grupo = {groupId} FOR UPDATE
                """).SingleOrDefaultAsync(cancellationToken);
            if (group is null || !group.Activo) return new CocinaResult(409, "DESTINO_STATE_CONFLICT");
            var tables = await context.Mesas.FromSqlInterpolated($"""
                SELECT xmin, * FROM mesa WHERE id_grupo = {groupId} ORDER BY id_mesa FOR UPDATE
                """).ToListAsync(cancellationToken);
            if (tables.Count < 2 || tables.Any(table => table.Estado != MesaStates.Agrupada))
                return new CocinaResult(409, "DESTINO_STATE_CONFLICT");
        }
        else if (destination.IdMesa is int mesaId)
        {
            var mesa = await context.Mesas.FromSqlInterpolated($"""
                SELECT xmin, * FROM mesa WHERE id_mesa = {mesaId} FOR UPDATE
                """).SingleOrDefaultAsync(cancellationToken);
            if (mesa is null || mesa.IdGrupo is not null || mesa.Estado != MesaStates.Ocupada)
                return new CocinaResult(409, "DESTINO_STATE_CONFLICT");
        }
        else return new CocinaResult(409, "DESTINO_STATE_CONFLICT");

        var pedido = await context.Pedidos.FromSqlInterpolated($"""
            SELECT * FROM pedido WHERE id_pedido = {destination.IdPedido} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        if (pedido is null || !PedidoStates.Active.Contains(pedido.Estado) ||
            pedido.IdMesa != destination.IdMesa || pedido.IdGrupo != destination.IdGrupo)
            return new CocinaResult(409, "DESTINO_STATE_CONFLICT");
        var command = await context.Comandas.FromSqlInterpolated($"""
            SELECT xmin, * FROM comanda WHERE id_comanda = {id} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        if (command is null || command.IdPedido != pedido.IdPedido) return new CocinaResult(404, "HTTP_NO_ENCONTRADO");
        if (command.Version != request.VersionComanda) return new CocinaResult(409, "VERSION_CONFLICT");
        if (!((command.Estado == ComandaStates.Abierta && request.Estado == ComandaStates.EnPreparacion) ||
              (command.Estado == ComandaStates.EnPreparacion && request.Estado == ComandaStates.Lista)))
            return new CocinaResult(409, "DESTINO_STATE_CONFLICT");

        var previous = command.Estado;
        command.Estado = request.Estado;
        await context.SaveChangesAsync(cancellationToken);
        var states = await context.Comandas.AsNoTracking().Where(item => item.IdPedido == pedido.IdPedido)
            .Select(item => item.Estado).ToListAsync(cancellationToken);
        pedido.Estado = states.Contains(ComandaStates.Abierta) ? PedidoStates.Abierto :
            states.Contains(ComandaStates.EnPreparacion) ? PedidoStates.EnPreparacion : PedidoStates.Listo;
        audit.Record("ComandaEstadoCambiado", "Exitoso", userId, entityType: "comanda", entityId: id.ToString(),
            details: new Dictionary<string, object?>
            {
                ["idPedido"] = pedido.IdPedido,
                ["estadoAnterior"] = previous,
                ["estadoNuevo"] = command.Estado
            });
        await context.SaveChangesAsync(cancellationToken);
        await context.Entry(command).Collection(item => item.Detalles).LoadAsync(cancellationToken);
        command.Pedido = pedido;
        if (pedido.IdMesa is int tableId)
            pedido.Mesa = await context.Mesas.SingleAsync(item => item.IdMesa == tableId, cancellationToken);
        var response = ToDto(command);
        await transaction.CommitAsync(cancellationToken);
        return new CocinaResult(200, Comanda: response);
    }

    private static CocinaComandaDto ToDto(Comanda command) => new(
        command.IdComanda, command.IdPedido, command.Pedido.IdMesa, command.Pedido.Mesa?.Numero,
        command.Pedido.IdGrupo, command.IdUsuario, command.FechaHora, command.Estado, command.Version,
        command.Detalles.OrderBy(item => item.IdDetalle)
            .Select(item => new CocinaLineaDto(item.IdDetalle, item.NombreProducto, item.Cantidad, item.Notas)).ToArray());

    private sealed record Cursor(string Tipo, DateTimeOffset Fecha, int Id);
}
