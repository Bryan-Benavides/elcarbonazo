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
public sealed record CocinaAlertasAnulacionPagina(IReadOnlyList<CocinaAlertaAnulacionDto> Items, string? CursorSiguiente);
public sealed record CocinaAlertaAnulacionDto(long IdAnulacion, int IdPedido, int IdComanda, int? IdMesa, int? IdGrupo,
    string Texto, string Motivo, DateTimeOffset FechaUtc, DateTimeOffset? FechaAcuseUtc);

public sealed class CocinaService(AppDbContext context, IAuditRecorder audit, TimeProvider? time = null)
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

    public async Task<(int Status, string? Code, CocinaAlertasAnulacionPagina? Pagina)> AlertasAnulacionAsync(
        string? cursor, int limite, CancellationToken ct, int? actorId = null, Guid? sessionId = null)
    {
        if (limite is < 1 or > 100) return (400, "VALIDATION_ERROR", null);
        if (actorId.HasValue || sessionId.HasValue)
        {
            if (actorId is null || sessionId is null ||
                !await SesionCocinaAutorizadaAsync(actorId.Value, sessionId.Value, ct))
                return (401, "AUTH_SESION_EXPIRADA", null);
        }
        long? lastId = null;
        if (cursor is not null)
        {
            try
            {
                if (cursor.Length is 0 or > 256 || cursor.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
                    return (400, "VALIDATION_ERROR", null);
                var encoded = cursor.Replace('-', '+').Replace('_', '/');
                encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
                var parsed = JsonSerializer.Deserialize<AlertaCursor>(Convert.FromBase64String(encoded), JsonOptions);
                if (parsed is not { Version: 1, Id: > 0 }) return (400, "VALIDATION_ERROR", null);
                lastId = parsed.Id;
            }
            catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
            {
                return (400, "VALIDATION_ERROR", null);
            }
        }
        var query = from alert in context.AlertasAnulacion.AsNoTracking()
                    join cancel in context.AnulacionesPedido.AsNoTracking() on alert.IdAnulacion equals cancel.IdAnulacion
                    join order in context.Pedidos.AsNoTracking() on cancel.IdPedido equals order.IdPedido
                    where alert.FechaAcuseUtc == null
                    select new { Alert = alert, Cancel = cancel, Order = order };
        if (lastId is long id) query = query.Where(x => x.Alert.IdAnulacion > id);
        var rows = await query.OrderBy(x => x.Alert.IdAnulacion).Take(limite + 1).ToListAsync(ct);
        var more = rows.Count > limite;
        if (more) rows.RemoveAt(rows.Count - 1);
        var items = rows.Select(x => new CocinaAlertaAnulacionDto(
            x.Alert.IdAnulacion, x.Cancel.IdPedido, x.Cancel.IdComanda,
            x.Order.IdMesa, x.Order.IdGrupo, "ANULADO — NO PREPARAR", x.Cancel.Motivo,
            x.Cancel.FechaUtc, x.Alert.FechaAcuseUtc)).ToArray();
        var next = more ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
            new AlertaCursor(1, items[^1].IdAnulacion), JsonOptions)).TrimEnd('=').Replace('+', '-').Replace('/', '_') : null;
        return (200, null, new(items, next));
    }

    public async Task<(int Status, string? Code)> AcusarAnulacionAsync(
        long id, int userId, CancellationToken ct, Guid? sessionId = null)
    {
        if (id <= 0 || userId <= 0) return (400, "VALIDATION_ERROR");
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        if (sessionId is Guid currentSession && !await SesionCocinaAutorizadaAsync(userId, currentSession, ct))
            return (401, "AUTH_SESION_EXPIRADA");
        var alert = await context.AlertasAnulacion.FromSqlInterpolated($"""
            SELECT * FROM alerta_anulacion WHERE id_anulacion = {id} FOR UPDATE
        """).SingleOrDefaultAsync(ct);
        if (sessionId is Guid revalidatedSession && !await SesionCocinaAutorizadaAsync(userId, revalidatedSession, ct))
            return (401, "AUTH_SESION_EXPIRADA");
        if (alert is null) return (404, "RECURSO_NO_ENCONTRADO");
        if (alert.FechaAcuseUtc is null)
        {
            var now = (time ?? TimeProvider.System).GetUtcNow();
            alert.FechaAcuseUtc = now;
            alert.IdUsuarioAcuse = userId;
            audit.Record("AlertaAnulacionAcusada", "Exitoso", userId, entityType: "anulacion", entityId: id.ToString());
            await context.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return (200, "OK");
    }

    private async Task<bool> SesionCocinaAutorizadaAsync(int userId, Guid sessionId, CancellationToken ct)
    {
        if (!await CajaSesion.VigenteAsync(context, sessionId, userId, (time ?? TimeProvider.System).GetUtcNow(), ct))
            return false;
        return await context.Usuarios.AsNoTracking().AnyAsync(x => x.IdUsuario == userId && x.Activo &&
            (x.Rol == UserRoles.Jefa || x.Rol == UserRoles.Supervisor || x.Rol == UserRoles.PersonalCocina), ct);
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
        if (await CajaPendientes.HayCobroParaCuentaAsync(context, pedido.IdPedido, Guid.Empty, cancellationToken))
            return new CocinaResult(409, "OPERACION_NO_TERMINAL");
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
        var commandsForOrder = await context.Comandas.AsNoTracking().Include(item => item.Detalles)
            .Where(item => item.IdPedido == pedido.IdPedido).ToListAsync(cancellationToken);
        var states = commandsForOrder.Where(item => item.Detalles.Any(detail => !detail.Cancelado))
            .Select(item => item.Estado).ToList();
        pedido.Estado = states.Count == 0 ? PedidoStates.Cancelado :
            states.Contains(ComandaStates.Abierta) ? PedidoStates.Abierto :
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
            .Where(item => !item.Cancelado)
            .Select(item => new CocinaLineaDto(item.IdDetalle, item.NombreProducto, item.Cantidad, item.Notas)).ToArray());

    private sealed record Cursor(string Tipo, DateTimeOffset Fecha, int Id);
    private sealed record AlertaCursor(int Version, long Id);
}
