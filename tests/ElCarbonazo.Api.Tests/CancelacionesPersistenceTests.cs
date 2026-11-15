using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class CancelacionesPersistenceTests
{
    private const string InventarioMigration = "20261114185615_AddInventarioEscandallo";

    [Fact]
    public async Task Migration_preserves_history_without_fabricating_consumption_and_blocks_down()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync(InventarioMigration);
            await using var context = database.CreateContext();
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO producto (id_producto, nombre, categoria, precio, disponible)
                VALUES (101, 'Plato histórico', 'Comida', 10, true);
                INSERT INTO pedido (id_pedido, id_mesa, id_usuario, fecha_hora, subtotal, impuestos, total, estado)
                VALUES (101, 1, 1, now(), 10, 0, 10, 'Pagado');
                INSERT INTO comanda (id_comanda, id_pedido, id_usuario, fecha_hora, estado)
                VALUES (101, 101, 1, now(), 'Lista');
                INSERT INTO detalle_pedido (id_detalle, id_comanda, id_producto, nombre_producto, cantidad, precio_unitario, subtotal, notas)
                VALUES (101, 101, 101, 'Plato histórico', 1, 10, 10, '');
                INSERT INTO ingrediente (id_ingrediente, nombre, nombre_clave, unidad_medida, stock_actual, stock_minimo, activo, fecha_creacion_utc, fecha_actualizacion_utc, version)
                VALUES (101, 'Carne', 'carne', 'lb', 9, 0, true, now(), now(), 1);
                INSERT INTO movimiento_inventario (id_ingrediente, tipo_movimiento, cantidad_delta, stock_anterior, stock_posterior, id_pedido, id_comanda, id_usuario, fecha_utc)
                VALUES (101, 'ConsumoPedido', -1, 10, 9, 101, 101, 1, now());
                INSERT INTO operacion_caja (id_operacion, id_usuario_propietario, tipo, version_contrato, argumentos, hash_argumentos, estado, fecha_creacion)
                VALUES ('00000000-0000-4000-8000-000000000101', 1, 'AbrirTurno', 1, '{{}}', repeat('a', 64), 'Preparada', now()),
                       ('00000000-0000-4000-8000-000000000102', 1, 'CobrarCuenta', 1, '{{}}', repeat('b', 64), 'Preparada', now());
                INSERT INTO turno_caja (id_turno_caja, estado, fecha_apertura, id_usuario_apertura, fondo_inicial, id_operacion_apertura)
                VALUES (101, 'Abierto', now(), 1, 0, '00000000-0000-4000-8000-000000000101');
                INSERT INTO cobro (id_cobro, numero_comprobante, id_pedido, id_turno_caja, id_usuario, id_operacion,
                    metodo_pago, subtotal, impuestos, total, efectivo_recibido, vuelto,
                    verificacion_externa_confirmada, referencia_repetida_confirmada, fecha_hora)
                VALUES (101, 101, 101, 101, 1, '00000000-0000-4000-8000-000000000102', 'Efectivo', 10, 0, 10, 10, 0, false, false, now());
                INSERT INTO comprobante_cobro_linea (id_cobro, orden, id_detalle_origen, nombre_producto, cantidad, precio_unitario, subtotal)
                VALUES (101, 1, 101, 'Plato histórico', 1, 10, 10);
                """);
            await context.Database.MigrateAsync();
            Assert.Equal(1, await context.Pedidos.CountAsync(x => x.IdPedido == 101 && x.Total == 10));
            Assert.Equal(1, await context.Comandas.CountAsync(x => x.IdComanda == 101 && x.RevisionDetalles == 1));
            Assert.Equal(1, await context.DetallesPedido.CountAsync(x => x.IdDetalle == 101 && !x.Cancelado));
            Assert.Equal(1, await context.MovimientosInventario.CountAsync(x => x.IdComanda == 101 && x.IdAnulacion == null));
            Assert.Equal(1, await context.Cobros.CountAsync(x => x.IdCobro == 101 && x.IdPedido == 101 && x.Total == 10));
            Assert.Equal(1, await context.ComprobantesCobroLineas.CountAsync(x => x.IdCobro == 101 && x.IdDetalleOrigen == 101 && x.Subtotal == 10));
            Assert.Empty(await context.ConsumosDetalle.ToListAsync());
            Assert.Empty(await context.AnulacionesPedido.ToListAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.MigrateAsync(InventarioMigration));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Constraints_reject_invalid_operations_and_snapshots()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO operacion_anulacion (id_operacion, id_usuario_propietario, estado, fecha_creacion_utc)
                VALUES (gen_random_uuid(), 1, 'Autorizada', now())
                """));
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO limite_autorizacion (id_usuario_solicita, hash_nombre, intentos_fallidos, ultima_actividad_utc)
                VALUES (1, repeat('a', 64), 6, now())
                """));
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO consumo_detalle (id_detalle, id_ingrediente, cantidad) VALUES (1, 1, 0)
                """));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Authorization_and_alert_constraints_enforce_identity_and_expiration()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO sesion_usuario (id_sesion, id_usuario, fecha_creacion, ultima_actividad, expiracion_absoluta)
                VALUES ('00000000-0000-4000-8000-000000000201', 1, now(), now(), now() + interval '1 hour');
                INSERT INTO operacion_anulacion (id_operacion, id_usuario_propietario, hash_intencion, estado, fecha_creacion_utc)
                VALUES ('00000000-0000-4000-8000-000000000202', 1, repeat('a', 64), 'Autorizada', now());
                INSERT INTO autorizacion_anulacion (id_operacion, id_sesion, id_usuario_autoriza, hash_token, fecha_emision_utc, fecha_expiracion_utc)
                VALUES ('00000000-0000-4000-8000-000000000202', '00000000-0000-4000-8000-000000000201',
                    1, repeat('b', 64), now(), now() + interval '2 minutes');
                """);
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO autorizacion_anulacion (id_operacion, id_sesion, id_usuario_autoriza, hash_token, fecha_emision_utc, fecha_expiracion_utc)
                VALUES ('00000000-0000-4000-8000-000000000202', '00000000-0000-4000-8000-000000000201',
                    1, repeat('b', 64), now(), now() + interval '2 minutes')
                """));
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO autorizacion_anulacion (id_operacion, id_sesion, id_usuario_autoriza, hash_token, fecha_emision_utc, fecha_expiracion_utc)
                VALUES ('00000000-0000-4000-8000-000000000202', '00000000-0000-4000-8000-000000000201',
                    1, repeat('c', 64), now(), now() + interval '121 seconds')
                """));
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                DELETE FROM sesion_usuario WHERE id_sesion = '00000000-0000-4000-8000-000000000201'
                """));
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO alerta_anulacion (id_anulacion, fecha_acuse_utc, id_usuario_acuse) VALUES (999, now(), NULL)
                """));
        }
        finally { await database.DisposeAsync(); }
    }
}
