using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ElCarbonazo.Api.Services;

public sealed record AnulacionEjecucionResult(int Status, string Code, string? Json = null);

public sealed class AnulacionEjecucionService(
    AppDbContext context, IAuditRecorder audit, TimeProvider time, IConfiguration configuration)
{
    private const decimal MaxMoney = 9999999999.99m;

    public async Task<AnulacionEjecucionResult> EjecutarAsync(
        int pedidoId, EjecutarAnulacionRequest request, int actorId, Guid sessionId, CancellationToken ct)
    {
        if (pedidoId <= 0 || request.IdOperacion == Guid.Empty || request.IdComanda <= 0 ||
            request.IdDetalle is <= 0 || string.IsNullOrEmpty(request.Token) || request.Token.Length > 128 ||
            actorId <= 0 || sessionId == Guid.Empty)
            return new(400, "VALIDATION_ERROR");
        var motive = NormalizeReason(request.Motivo);
        if (motive is null) return new(400, "VALIDATION_ERROR");
        byte[] key;
        try { key = Convert.FromBase64String(configuration["AutorizacionSuperior:ClaveHmac"] ?? string.Empty); }
        catch (FormatException) { return new(503, "AUTORIZACION_SUPERIOR_NO_DISPONIBLE"); }
        if (key.Length < 32) return new(503, "AUTORIZACION_SUPERIOR_NO_DISPONIBLE");

        var hash = IntentHash(request.IdOperacion, pedidoId, request.IdComanda, request.IdDetalle, motive, actorId);
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));
        var result = new AnulacionEjecucionResult(409, "TOKEN_SUPERIOR_EXPIRADO_O_USADO");
        try
        {
            var guard = new HmacAutorizacionGuard(context, time);
            var state = await guard.EjecutarAsync(key, async innerCt =>
            {
                result = await EjecutarEnTransaccionAsync(pedidoId, request, motive, hash, tokenHash, actorId, sessionId, innerCt);
            }, ct);
            return state == EstadoHmacAutorizacion.Disponible
                ? result
                : new(state == EstadoHmacAutorizacion.Espera ? 403 : 503, "AUTORIZACION_SUPERIOR_NO_DISPONIBLE");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private async Task<AnulacionEjecucionResult> EjecutarEnTransaccionAsync(
        int pedidoId, EjecutarAnulacionRequest request, string motive, string intentionHash, string tokenHash,
        int actorId, Guid sessionId, CancellationToken ct)
    {
        await AnulacionOperacionLock.AdquirirAsync(context, request.IdOperacion, ct);
        var operation = await context.OperacionesAnulacion.FromSqlInterpolated(
            $"SELECT * FROM operacion_anulacion WHERE id_operacion = {request.IdOperacion} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (operation is null || operation.IdUsuarioPropietario != actorId)
            return new(404, "RECURSO_NO_ENCONTRADO");
        if (operation.HashIntencion != intentionHash) return new(409, "OPERACION_CLAVE_EN_USO");
        if (operation.Estado != "Autorizada")
            return new(operation.StatusResultado ?? 409, operation.CodigoResultado ?? "OPERACION_TERMINAL", operation.ResultadoJson);
        if (!await CajaSesion.VigenteAsync(context, sessionId, actorId, time.GetUtcNow(), ct))
            return new(401, "AUTH_SESION_EXPIRADA");
        var actor = await context.Usuarios.AsNoTracking().SingleOrDefaultAsync(x => x.IdUsuario == actorId && x.Activo &&
            (x.Rol == UserRoles.Mesero || x.Rol == UserRoles.EncargadoCaja || x.Rol == UserRoles.Jefa || x.Rol == UserRoles.Supervisor), ct);
        if (actor is null) return new(403, "AUTH_PROHIBIDO");
        var candidates = await context.AutorizacionesAnulacion.FromSqlInterpolated($"""
            SELECT * FROM autorizacion_anulacion
            WHERE id_operacion = {request.IdOperacion} AND id_sesion = {sessionId}
              AND fecha_consumo_utc IS NULL
            FOR UPDATE
            """).ToListAsync(ct);
        var presentedHash = Convert.FromHexString(tokenHash);
        AutorizacionAnulacion? auth = null;
        foreach (var candidate in candidates)
        {
            if (CryptographicOperations.FixedTimeEquals(Convert.FromHexString(candidate.HashToken), presentedHash))
                auth ??= candidate;
        }
        if (auth is null || auth.FechaExpiracionUtc <= time.GetUtcNow())
            return new(409, "TOKEN_SUPERIOR_EXPIRADO_O_USADO");
        var jefa = await context.Usuarios.AsNoTracking().AnyAsync(x => x.IdUsuario == auth.IdUsuarioAutoriza &&
            x.Activo && x.Rol == UserRoles.Jefa, ct);
        if (!jefa) return new(409, "TOKEN_SUPERIOR_EXPIRADO_O_USADO");

        var tx = context.Database.CurrentTransaction!;
        await tx.CreateSavepointAsync("anulacion_dominio", ct);
        await CajaLocks.CajaCompartidaAsync(context, ct);
        if (!await CajaSesion.VigenteAsync(context, sessionId, actorId, time.GetUtcNow(), ct) ||
            !await context.Usuarios.AnyAsync(x => x.IdUsuario == actorId && x.Activo &&
                (x.Rol == UserRoles.Mesero || x.Rol == UserRoles.EncargadoCaja || x.Rol == UserRoles.Jefa || x.Rol == UserRoles.Supervisor), ct))
            return new(401, "AUTH_SESION_EXPIRADA");

        var dest = await context.Pedidos.AsNoTracking().Where(x => x.IdPedido == pedidoId)
            .Select(x => new { x.IdMesa, x.IdGrupo }).SingleOrDefaultAsync(ct);
        if (dest is null) return await RechazarAsync(404, "RECURSO_NO_ENCONTRADO");
        if (dest.IdGrupo is int groupId)
        {
            var group = await context.GruposMesa.FromSqlInterpolated(
                $"SELECT * FROM grupo_mesa WHERE id_grupo = {groupId} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (group is null) return await RechazarAsync(409, "DESTINO_STATE_CONFLICT");
            await context.Mesas.FromSqlInterpolated($"""
                SELECT xmin, * FROM mesa WHERE id_grupo = {groupId} ORDER BY id_mesa FOR UPDATE
                """).ToListAsync(ct);
        }
        else if (dest.IdMesa is int mesaId)
        {
            var mesa = await context.Mesas.FromSqlInterpolated(
                $"SELECT xmin, * FROM mesa WHERE id_mesa = {mesaId} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (mesa is null) return await RechazarAsync(409, "DESTINO_STATE_CONFLICT");
        }
        else return await RechazarAsync(409, "DESTINO_STATE_CONFLICT");

        var order = await context.Pedidos.FromSqlInterpolated(
            $"SELECT * FROM pedido WHERE id_pedido = {pedidoId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (order is null || order.IdMesa != dest.IdMesa || order.IdGrupo != dest.IdGrupo)
            return await RechazarAsync(409, "DESTINO_STATE_CONFLICT");
        if (order.Estado == PedidoStates.Pagado ||
            await context.Cobros.AsNoTracking().AnyAsync(x => x.IdPedido == pedidoId, ct))
            return await RechazarAsync(409, "CUENTA_YA_PAGADA");
        if (!PedidoStates.Active.Contains(order.Estado)) return await RechazarAsync(409, "DESTINO_STATE_CONFLICT");
        if (await CajaPendientes.HayCobroParaCuentaAsync(context, pedidoId, Guid.Empty, ct))
            return await RechazarAsync(409, "OPERACION_NO_TERMINAL");

        var commands = await context.Comandas.FromSqlInterpolated($"""
            SELECT xmin, * FROM comanda WHERE id_pedido = {pedidoId} ORDER BY id_comanda FOR UPDATE
            """).ToListAsync(ct);
        var command = commands.SingleOrDefault(x => x.IdComanda == request.IdComanda);
        if (command is null) return await RechazarAsync(404, "RECURSO_NO_ENCONTRADO");
        if (command.Estado is not ("Abierta" or "En Preparación" or "Lista"))
            return await RechazarAsync(409, "DESTINO_STATE_CONFLICT");
        var details = await context.DetallesPedido.FromSqlInterpolated($"""
            SELECT * FROM detalle_pedido WHERE id_comanda = {command.IdComanda} ORDER BY id_detalle FOR UPDATE
            """).ToListAsync(ct);
        var selected = request.IdDetalle is int detailId
            ? details.Where(x => x.IdDetalle == detailId && !x.Cancelado).ToArray()
            : details.Where(x => !x.Cancelado).ToArray();
        if (selected.Length == 0) return await RechazarAsync(409, "ITEM_YA_CANCELADO");

        var allDetails = await context.DetallesPedido.AsNoTracking()
            .Where(x => x.IdComanda == command.IdComanda).ToListAsync(ct);
        var snapshots = await context.ConsumosDetalle.AsNoTracking()
            .Where(x => allDetails.Select(d => d.IdDetalle).Contains(x.IdDetalle))
            .ToListAsync(ct);
        if (allDetails.Any(d => !snapshots.Any(s => s.IdDetalle == d.IdDetalle)))
            return await RechazarAsync(409, "EVIDENCIA_CONSUMO_INSUFICIENTE");
        var consumption = snapshots.GroupBy(x => x.IdIngrediente).ToDictionary(g => g.Key, g => g.Sum(x => x.Cantidad));
        var movements = await context.MovimientosInventario.AsNoTracking()
            .Where(x => x.IdComanda == command.IdComanda && x.TipoMovimiento == "ConsumoPedido")
            .GroupBy(x => x.IdIngrediente).Select(g => new { Id = g.Key, Quantity = -g.Sum(x => x.CantidadDelta) }).ToListAsync(ct);
        if (consumption.Count != movements.Count || movements.Any(x => consumption.GetValueOrDefault(x.Id) != x.Quantity))
            return await RechazarAsync(409, "EVIDENCIA_CONSUMO_INSUFICIENTE");

        var selectedIds = selected.Select(x => x.IdDetalle).ToArray();
        var returning = snapshots.Where(x => selectedIds.Contains(x.IdDetalle))
            .GroupBy(x => x.IdIngrediente).ToDictionary(g => g.Key, g => g.Sum(x => x.Cantidad));
        var ingredientIds = consumption.Keys.Order().ToArray();
        var ingredients = await context.Ingredientes.FromSqlInterpolated($"""
            SELECT * FROM ingrediente WHERE id_ingrediente = ANY({ingredientIds}) ORDER BY id_ingrediente FOR UPDATE
            """).ToListAsync(ct);
        if (ingredients.Count != ingredientIds.Length || ingredients.Any(x => x.Version == long.MaxValue))
            return await RechazarAsync(409, "VERSION_CONFLICT");
        var prior = await context.MovimientosInventario.AsNoTracking()
            .Where(x => x.IdComanda == command.IdComanda && x.TipoMovimiento == "ReversionCancelacion")
            .GroupBy(x => x.IdIngrediente).Select(g => new { Id = g.Key, Quantity = g.Sum(x => x.CantidadDelta) }).ToListAsync(ct);
        if (prior.Any(x => x.Quantity + returning.GetValueOrDefault(x.Id) > consumption.GetValueOrDefault(x.Id)))
            return await RechazarAsync(409, "EVIDENCIA_CONSUMO_INSUFICIENTE");

        decimal amount = 0;
        var units = 0;
        try
        {
            foreach (var detail in selected)
            {
                amount = checked(amount + detail.Subtotal);
                units = checked(units + detail.Cantidad);
            }
            if (amount > MaxMoney) return await RechazarAsync(400, "VALIDATION_ERROR");
        }
        catch (OverflowException) { return await RechazarAsync(400, "VALIDATION_ERROR"); }

        var now = time.GetUtcNow();
        var annulment = new AnulacionPedido
        {
            IdPedido = pedidoId,
            IdComanda = command.IdComanda,
            IdDetalle = request.IdDetalle,
            CantidadAnulada = units,
            MontoRevertido = amount,
            EstadoComandaPrevio = command.Estado,
            IdUsuarioSolicita = actorId,
            IdUsuarioAutoriza = auth.IdUsuarioAutoriza,
            Motivo = motive,
            InventarioRevertido = true,
            FechaUtc = now
        };
        context.AnulacionesPedido.Add(annulment);
        foreach (var detail in selected) detail.Cancelado = true;
        if (details.All(x => x.Cancelado)) command.Estado = "Cancelada";
        if (command.RevisionDetalles == long.MaxValue) return await RechazarAsync(409, "VERSION_CONFLICT");
        command.RevisionDetalles++;
        var otherCommandDetails = await context.DetallesPedido.AsNoTracking()
            .Where(x => x.IdComanda != command.IdComanda && commands.Select(c => c.IdComanda).Contains(x.IdComanda) && !x.Cancelado)
            .Select(x => x.Subtotal).ToListAsync(ct);
        var remainCommand = details.Where(x => !x.Cancelado).Sum(x => x.Subtotal);
        order.Subtotal = otherCommandDetails.Sum() + remainCommand;
        order.Total = order.Subtotal;
        order.Impuestos = 0;
        foreach (var other in commands.Where(x => x.IdComanda != command.IdComanda))
        {
            var hasActive = await context.DetallesPedido.AsNoTracking().AnyAsync(x => x.IdComanda == other.IdComanda && !x.Cancelado, ct);
            if (!hasActive) other.Estado = "Cancelada";
        }
        var activeCommands = commands.Where(x => x.Estado != "Cancelada").ToArray();
        order.Estado = activeCommands.Length == 0 ? PedidoStates.Cancelado :
            activeCommands.Any(x => x.Estado == ComandaStates.Abierta) ? PedidoStates.Abierto :
            activeCommands.Any(x => x.Estado == ComandaStates.EnPreparacion) ? PedidoStates.EnPreparacion : PedidoStates.Listo;
        await context.SaveChangesAsync(ct);
        foreach (var ingredient in ingredients)
        {
            var quantity = returning.GetValueOrDefault(ingredient.IdIngrediente);
            if (quantity == 0) continue;
            var before = ingredient.StockActual;
            if (before + quantity > 99999999.9999m) return await RechazarAsync(400, "VALIDATION_ERROR");
            ingredient.StockActual += quantity;
            ingredient.Version++;
            ingredient.FechaActualizacionUtc = now;
            context.MovimientosInventario.Add(new MovimientoInventario
            {
                IdIngrediente = ingredient.IdIngrediente,
                TipoMovimiento = "ReversionCancelacion",
                CantidadDelta = quantity,
                StockAnterior = before,
                StockPosterior = ingredient.StockActual,
                IdPedido = pedidoId,
                IdComanda = command.IdComanda,
                IdAnulacion = annulment.IdAnulacion,
                IdUsuario = actorId,
                Motivo = motive,
                FechaUtc = now
            });
        }
        context.AlertasAnulacion.Add(new AlertaAnulacion { IdAnulacion = annulment.IdAnulacion });
        auth.FechaConsumoUtc = now;
        var responseJson = JsonSerializer.Serialize(new
        {
            estado = "Completada",
            codigo = "ANULACION_COMPLETADA",
            idAnulacion = annulment.IdAnulacion,
            pedidoId,
            comandaId = command.IdComanda,
            detalleId = request.IdDetalle,
            montoRevertido = amount,
            subtotal = order.Subtotal,
            total = order.Total
        });
        operation.Estado = "Completada";
        operation.FechaTerminalUtc = now;
        operation.StatusResultado = 200;
        operation.CodigoResultado = "ANULACION_COMPLETADA";
        operation.ResultadoJson = responseJson;
        audit.Record("AnulacionPedido", "Exitoso", actorId, sessionId, "anulacion", annulment.IdAnulacion.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, object?>
            {
                ["idPedido"] = pedidoId,
                ["idComanda"] = command.IdComanda,
                ["idDetalle"] = request.IdDetalle,
                ["montoRevertido"] = amount,
                ["inventarioRevertido"] = true
            });
        await context.SaveChangesAsync(ct);
        return new(200, "ANULACION_COMPLETADA", responseJson);

        async Task<AnulacionEjecucionResult> RechazarAsync(int status, string code)
        {
            await tx.RollbackToSavepointAsync("anulacion_dominio", ct);
            context.ChangeTracker.Clear();
            var freshOperation = await context.OperacionesAnulacion.SingleAsync(x => x.IdOperacion == request.IdOperacion, ct);
            var freshAuth = await context.AutorizacionesAnulacion.SingleAsync(x => x.IdAutorizacion == auth!.IdAutorizacion, ct);
            freshAuth.FechaConsumoUtc = time.GetUtcNow();
            freshOperation.Estado = "Rechazada";
            freshOperation.FechaTerminalUtc = time.GetUtcNow();
            freshOperation.StatusResultado = (short)status;
            freshOperation.CodigoResultado = code;
            freshOperation.ResultadoJson = JsonSerializer.Serialize(new { estado = "Rechazada", codigo = code });
            audit.Record("AnulacionPedido", "Rechazado", actorId, sessionId, "OperacionAnulacion", request.IdOperacion.ToString("D"));
            await context.SaveChangesAsync(ct);
            return new(status, code, freshOperation.ResultadoJson);
        }
    }

    private static string IntentHash(Guid operationId, int orderId, int commandId, int? detailId, string reason, int actorId)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            actorId,
            IdOperacion = operationId,
            IdPedido = orderId,
            IdComanda = commandId,
            IdDetalle = detailId,
            motivo = reason
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string? NormalizeReason(string? value)
    {
        if (value is null) return null;
        try { value = value.Normalize(NormalizationForm.FormC).Trim(); }
        catch (ArgumentException) { return null; }
        return value.EnumerateRunes().Count() is >= 5 and <= 250 && !value.EnumerateRunes().Any(Rune.IsControl) ? value : null;
    }
}
