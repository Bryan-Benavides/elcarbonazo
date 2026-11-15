using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class InventarioPersistenceTests
{
    private const string CajaMigration = "20261109224603_AddCajaPersistence";

    [Fact]
    public async Task Migration_preserves_existing_product_and_does_not_invent_inventory()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync(CajaMigration);
            await using var context = database.CreateContext();
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO producto (nombre, categoria, precio, disponible)
                VALUES ('Producto histórico', 'Comida', 10, true)
                """);
            await context.Database.MigrateAsync();
            Assert.Equal(1, await context.Productos.CountAsync());
            Assert.Empty(await context.Ingredientes.ToListAsync());
            Assert.Empty(await context.Recetas.ToListAsync());
            Assert.Empty(await context.MovimientosInventario.ToListAsync());
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Constraints_reject_invalid_stock_duplicates_and_invalid_ledger_entries()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO ingrediente (nombre, nombre_clave, unidad_medida, fecha_creacion_utc, fecha_actualizacion_utc)
                VALUES ('Carne', 'carne', 'lb', now(), now())
                """);
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO ingrediente (nombre, nombre_clave, unidad_medida, fecha_creacion_utc, fecha_actualizacion_utc)
                VALUES ('CARNE', 'carne', 'lb', now(), now())
                """));
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE ingrediente SET stock_actual = -1"));
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO movimiento_inventario (id_ingrediente, tipo_movimiento, cantidad_delta, stock_anterior, stock_posterior, id_usuario, fecha_utc)
                SELECT id_ingrediente, 'AjustePositivo', 1, 0, 1, 1, now() FROM ingrediente
                """));
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO movimiento_inventario (id_ingrediente, tipo_movimiento, cantidad_delta, stock_anterior, stock_posterior, id_usuario, fecha_utc)
                SELECT id_ingrediente, 'EntradaCompra', 1, 0, 2, 1, now() FROM ingrediente
                """));
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO movimiento_inventario (id_ingrediente, tipo_movimiento, cantidad_delta, stock_anterior, stock_posterior, id_usuario, fecha_utc)
                SELECT id_ingrediente, 'EntradaCompra', 0.3500, 0, 0.3500, 1, now() FROM ingrediente
                """);
            Assert.Equal(0.3500m, (await context.MovimientosInventario.SingleAsync()).CantidadDelta);
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO movimiento_inventario (id_ingrediente, tipo_movimiento, cantidad_delta, stock_anterior, stock_posterior, id_usuario, fecha_utc)
                SELECT id_ingrediente, 'ConsumoPedido', -0.1, 1, 0.9, 1, now() FROM ingrediente
                """));
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE ingrediente SET unidad_medida = 'litros'"));
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO producto (nombre, categoria, precio, disponible) VALUES ('Plato', 'Comida', 10, true);
                INSERT INTO receta (id_producto, fecha_actualizacion_utc) SELECT id_producto, now() FROM producto;
                INSERT INTO detalle_receta (id_receta, id_ingrediente, cantidad_requerida)
                SELECT r.id_receta, i.id_ingrediente, 0.3500 FROM receta r CROSS JOIN ingrediente i;
                """);
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO receta (id_producto, fecha_actualizacion_utc)
                SELECT id_producto, now() FROM producto
                """));
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO detalle_receta (id_receta, id_ingrediente, cantidad_requerida)
                SELECT id_receta, id_ingrediente, cantidad_requerida FROM detalle_receta
                """));
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE detalle_receta SET cantidad_requerida = 0"));
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("DELETE FROM ingrediente"));
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE receta SET version = 0"));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Migration_preserves_order_command_cash_and_receipt_history()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync(CajaMigration);
            await using var context = database.CreateContext();
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO producto (nombre, categoria, precio, disponible)
                VALUES ('Producto histórico', 'Comida', 10, true)
                """);
            var product = await context.Productos.SingleAsync();
            var audit = new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System);
            var mesa = await context.Mesas.SingleAsync(x => x.IdMesa == 1);
            mesa.Estado = MesaStates.Ocupada;
            var historicOrder = new Pedido
            {
                IdMesa = 1,
                IdUsuario = 1,
                FechaHora = DateTimeOffset.UtcNow,
                Estado = PedidoStates.Abierto,
                Subtotal = 10m,
                Total = 10m
            };
            var historicCommand = new Comanda
            {
                Pedido = historicOrder,
                IdUsuario = 1,
                FechaHora = DateTimeOffset.UtcNow,
                Estado = "Abierta"
            };
            historicCommand.Detalles.Add(new DetallePedido
            {
                IdProducto = product.IdProducto,
                NombreProducto = product.Nombre,
                Cantidad = 1,
                PrecioUnitario = 10m,
                Subtotal = 10m,
                Notas = ""
            });
            context.Comandas.Add(historicCommand);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var command = await context.Comandas.AsNoTracking().SingleAsync();
            var kitchen = new CocinaService(context, audit);
            var preparing = await kitchen.CambiarEstadoAsync(command.IdComanda,
                new CambiarComandaEstadoRequest("En Preparación", command.Version), 1, default);
            Assert.Equal(200, preparing.Status);
            var ready = await kitchen.CambiarEstadoAsync(command.IdComanda,
                new CambiarComandaEstadoRequest("Lista", preparing.Comanda!.Version), 1, default);
            Assert.Equal(200, ready.Status);
            context.ChangeTracker.Clear();
            var opening = new CajaOperacionesService(context, audit, TimeProvider.System,
                new CajaAperturaExecutor(context, audit, TimeProvider.System));
            var openingId = Guid.NewGuid();
            Assert.Equal(200, (await opening.PrepararAsync(openingId, 1, CajaOperacionCanonica.Abrir(0, true), default)).Status);
            Assert.Equal(201, (await opening.EjecutarAsync(openingId, 1, default)).Status);
            context.ChangeTracker.Clear();
            var shift = await context.TurnosCaja.AsNoTracking().SingleAsync();
            var order = await context.Pedidos.AsNoTracking().Include(x => x.Comandas)
                .ThenInclude(x => x.Detalles).SingleAsync();
            var fingerprint = CajaElegibilidad.Evaluar(order).HuellaLineas;
            Assert.NotNull(fingerprint);
            var payment = new CajaOperacionesService(context, audit, TimeProvider.System,
                new CajaCobroExecutor(context, audit, TimeProvider.System));
            var paymentId = Guid.NewGuid();
            var paymentIntent = CajaOperacionCanonica.Cobrar(order.IdPedido, shift.IdTurnoCaja, order.Total,
                fingerprint, CajaStates.Efectivo, 10m, null, false, false);
            Assert.Equal(200, (await payment.PrepararAsync(paymentId, 1, paymentIntent, default)).Status);
            Assert.Equal(201, (await payment.EjecutarAsync(paymentId, 1, default)).Status);
            context.ChangeTracker.Clear();

            var beforeOrder = await context.Pedidos.AsNoTracking().Select(x => new { x.IdPedido, x.Estado, x.Total }).SingleAsync();
            var beforeCommand = await context.Comandas.AsNoTracking().Select(x => new { x.IdComanda, x.IdPedido, x.Estado }).SingleAsync();
            var beforeDetail = await context.DetallesPedido.AsNoTracking().Select(x => new { x.IdDetalle, x.IdComanda, x.NombreProducto, x.Subtotal }).SingleAsync();
            var beforePayment = await context.Cobros.AsNoTracking().Select(x => new { x.IdCobro, x.IdPedido, x.IdTurnoCaja, x.Total }).SingleAsync();
            var beforeReceipt = await context.ComprobantesCobroLineas.AsNoTracking().CountAsync();
            Assert.True(beforeReceipt > 0);
            await context.Database.MigrateAsync();
            context.ChangeTracker.Clear();
            Assert.Equal(beforeOrder, await context.Pedidos.AsNoTracking().Select(x => new { x.IdPedido, x.Estado, x.Total }).SingleAsync());
            Assert.Equal(beforeCommand, await context.Comandas.AsNoTracking().Select(x => new { x.IdComanda, x.IdPedido, x.Estado }).SingleAsync());
            Assert.Equal(beforeDetail, await context.DetallesPedido.AsNoTracking().Select(x => new { x.IdDetalle, x.IdComanda, x.NombreProducto, x.Subtotal }).SingleAsync());
            Assert.Equal(beforePayment, await context.Cobros.AsNoTracking().Select(x => new { x.IdCobro, x.IdPedido, x.IdTurnoCaja, x.Total }).SingleAsync());
            Assert.Equal(beforeReceipt, await context.ComprobantesCobroLineas.CountAsync());
            Assert.Empty(await context.MovimientosInventario.ToListAsync());
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO ingrediente (nombre, nombre_clave, unidad_medida, fecha_creacion_utc, fecha_actualizacion_utc)
                VALUES ('Carne', 'carne', 'lb', now(), now())
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO movimiento_inventario
                    (id_ingrediente, tipo_movimiento, cantidad_delta, stock_anterior, stock_posterior, id_pedido, id_comanda, id_usuario, fecha_utc)
                SELECT id_ingrediente, 'ConsumoPedido', -0.3500, 1, 0.6500, {beforeOrder.IdPedido}, {beforeCommand.IdComanda}, 1, now() FROM ingrediente
                """);
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO movimiento_inventario
                    (id_ingrediente, tipo_movimiento, cantidad_delta, stock_anterior, stock_posterior, id_pedido, id_comanda, id_usuario, fecha_utc)
                SELECT id_ingrediente, 'ConsumoPedido', -0.3500, 1, 0.6500, {beforeOrder.IdPedido}, {beforeCommand.IdComanda}, 1, now() FROM ingrediente
                """));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.MigrateAsync(CajaMigration));
            Assert.Equal(1, await context.MovimientosInventario.CountAsync());
            Assert.Equal(beforePayment, await context.Cobros.AsNoTracking().Select(x => new { x.IdCobro, x.IdPedido, x.IdTurnoCaja, x.Total }).SingleAsync());
        }
        finally { await database.DisposeAsync(); }
    }
}
