using ElCarbonazo.Api.Configuration;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class AnulacionEjecucionTests
{
    [Fact]
    public async Task Authorized_execution_reverses_exact_snapshot_once_and_replays_terminal_result()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var context = db.CreateContext();
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO producto (nombre, categoria, precio, disponible) VALUES ('Plato', 'Comida', 10, true)
                """);
            var product = await context.Productos.AsNoTracking().SingleAsync();
            await InventarioTestSetup.PrepararProductoAsync(context, product.IdProducto);
            var ingredient = await context.Ingredientes.AsNoTracking().SingleAsync();
            var table = await context.Mesas.AsNoTracking().SingleAsync(x => x.IdMesa == 1);
            var audit = new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System);
            var send = await new EnviosService(context, audit, TimeProvider.System).EnviarAsync(
                new(Guid.NewGuid(), 1, null, table.Version,
                    [new(product.IdProducto, product.Version, 1, ""), new(product.IdProducto, product.Version, 1, "")]), 1, default);
            Assert.Equal(201, send.Status);
            context.ChangeTracker.Clear();
            var order = await context.Pedidos.AsNoTracking().SingleAsync();
            var command = await context.Comandas.AsNoTracking().SingleAsync();
            var details = await context.DetallesPedido.AsNoTracking().OrderBy(x => x.IdDetalle).ToListAsync();
            Assert.Equal(2, details.Count);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE comanda SET estado = 'Lista' WHERE id_comanda = {command.IdComanda};
                UPDATE pedido SET estado = 'Listo' WHERE id_pedido = {order.IdPedido}
                """);
            var session = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO sesion_usuario (id_sesion, id_usuario, fecha_creacion, ultima_actividad, expiracion_absoluta)
                VALUES ({session}, 1, now(), now(), now() + interval '1 hour')
                """);
            var key = Enumerable.Repeat((byte)73, 32).ToArray();
            Assert.Equal(ResultadoProvisionHmac.Provisionado,
                await new HmacAutorizacionProvisioner(context, audit).ProvisionarInicialAsync(key, default));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AutorizacionSuperior:ClaveHmac"] = Convert.ToBase64String(key)
            }).Build();
            var passwords = new BCryptPasswordHasher(Options.Create(new PasswordHashingOptions { WorkFactor = 10 }));
            var operationId = Guid.NewGuid();
            var reason = "Pedido duplicado";
            var authorization = await new AnulacionAutorizacionService(context, passwords, audit, TimeProvider.System, configuration)
                .AutorizarAsync(new(operationId, order.IdPedido, command.IdComanda, details[0].IdDetalle, reason,
                    "jefa@example.local", "correct horse battery staple"), 1, session, default);
            Assert.Equal(200, authorization.Status);
            var request = new EjecutarAnulacionRequest(operationId, command.IdComanda, details[0].IdDetalle, reason, authorization.Data!.Token);
            var service = new AnulacionEjecucionService(context, audit, TimeProvider.System, configuration);
            var failing = new AnulacionEjecucionService(context, new FailingAudit(), TimeProvider.System, configuration);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                failing.EjecutarAsync(order.IdPedido, request, 1, session, default));
            await using (var verify = db.CreateContext())
            {
                Assert.Equal(99997m, (await verify.Ingredientes.SingleAsync()).StockActual);
                Assert.Empty(await verify.AnulacionesPedido.ToListAsync());
                Assert.Equal("Listo", (await verify.Pedidos.SingleAsync()).Estado);
                Assert.Null((await verify.AutorizacionesAnulacion.SingleAsync()).FechaConsumoUtc);
            }
            context.ChangeTracker.Clear();
            var result = await service.EjecutarAsync(order.IdPedido, request, 1, session, default);
            Assert.Equal(200, result.Status);
            context.ChangeTracker.Clear();
            Assert.Equal(99998m, (await context.Ingredientes.SingleAsync()).StockActual);
            Assert.Equal(1, await context.MovimientosInventario.CountAsync(x => x.TipoMovimiento == "ReversionCancelacion"));
            Assert.Single(await context.DetallesPedido.Where(x => x.Cancelado).ToListAsync());
            Assert.Equal("Lista", (await context.Comandas.SingleAsync()).Estado);
            Assert.Equal("Listo", (await context.Pedidos.SingleAsync()).Estado);

            var secondOperation = Guid.NewGuid();
            var secondAuthorization = await new AnulacionAutorizacionService(context, passwords, audit, TimeProvider.System, configuration)
                .AutorizarAsync(new(secondOperation, order.IdPedido, command.IdComanda, details[1].IdDetalle, reason,
                    "jefa@example.local", "correct horse battery staple"), 1, session, default);
            Assert.Equal(200, secondAuthorization.Status);
            var secondRequest = new EjecutarAnulacionRequest(secondOperation, command.IdComanda, details[1].IdDetalle, reason,
                secondAuthorization.Data!.Token);
            var secondResult = await service.EjecutarAsync(order.IdPedido, secondRequest, 1, session, default);
            Assert.Equal(200, secondResult.Status);
            context.ChangeTracker.Clear();
            Assert.Equal(99999m, (await context.Ingredientes.SingleAsync()).StockActual);
            Assert.Equal(2, await context.MovimientosInventario.CountAsync(x => x.TipoMovimiento == "ReversionCancelacion"));
            Assert.All(await context.DetallesPedido.ToListAsync(), detail => Assert.True(detail.Cancelado));
            Assert.Equal("Cancelada", (await context.Comandas.SingleAsync()).Estado);
            Assert.Equal("Cancelado", (await context.Pedidos.SingleAsync()).Estado);
            Assert.Equal(2, await context.AlertasAnulacion.CountAsync());
            var alertIds = await context.AlertasAnulacion.Select(x => x.IdAnulacion).Order().ToArrayAsync();
            Assert.Equal(2, alertIds.Length);
            var queries = new AnulacionesConsultasService(context, TimeProvider.System);
            var cancellationHistory = await queries.AnulacionesAsync(null, null, null, 25, default);
            Assert.Equal(2, cancellationHistory.Page!.Items.Count);
            Assert.All(cancellationHistory.Page.Items, item => Assert.Equal(reason, item.Motivo));
            Assert.Equal(400, (await queries.EventosAsync(
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(-1), null, null, 25, default)).Status);
            var kitchen = new CocinaService(context, audit, TimeProvider.System);
            var alerts = await kitchen.AlertasAnulacionAsync(null, 25, default);
            Assert.Equal(2, alerts.Pagina!.Items.Count);
            Assert.Equal(alertIds.Order(), alerts.Pagina.Items.Select(x => x.IdAnulacion).Order());
            Assert.Equal("ANULADO — NO PREPARAR", alerts.Pagina.Items[0].Texto);
            foreach (var alertId in alertIds)
            {
                Assert.Equal((200, "OK"), await kitchen.AcusarAnulacionAsync(alertId, 2, default));
                Assert.Equal((200, "OK"), await kitchen.AcusarAnulacionAsync(alertId, 2, default));
            }
            Assert.Empty((await kitchen.AlertasAnulacionAsync(null, 25, default)).Pagina!.Items);
            var replay = await service.EjecutarAsync(order.IdPedido, request, 1, session, default);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(result.Json!), JsonNode.Parse(replay.Json!)));
            Assert.Equal(2, await context.MovimientosInventario.CountAsync(x => x.TipoMovimiento == "ReversionCancelacion"));
        }
        finally { await db.DisposeAsync(); }
    }

    private sealed class FailingAudit : IAuditRecorder
    {
        public void Record(string eventType, string result, int? userId = null, Guid? sessionId = null,
            string? entityType = null, string? entityId = null, IReadOnlyDictionary<string, object?>? details = null) =>
            throw new InvalidOperationException("Audit failure");
    }
}
