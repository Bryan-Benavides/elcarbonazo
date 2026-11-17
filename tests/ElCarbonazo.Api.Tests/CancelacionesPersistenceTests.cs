using Microsoft.EntityFrameworkCore;
using ElCarbonazo.Api.Security;
using Npgsql;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class CancelacionesPersistenceTests
{
    private const string InventarioMigration = "20261114185615_AddInventarioEscandallo";

    [Fact]
    public async Task Hmac_guard_requires_provisioning_and_keeps_rotation_barrier_across_contexts()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            var clock = new TestClock { Now = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero) };
            var keyA = Enumerable.Repeat((byte)1, 32).ToArray();
            var keyB = Enumerable.Repeat((byte)2, 32).ToArray();
            var calls = 0;
            await using (var context = database.CreateContext())
            {
                var guard = new HmacAutorizacionGuard(context, clock);
                Assert.Equal(EstadoHmacAutorizacion.ClaveInvalida,
                    await guard.EjecutarAsync(new byte[31], _ => { calls++; return Task.CompletedTask; }, default));
                Assert.Equal(EstadoHmacAutorizacion.NoProvisionado,
                    await guard.EjecutarAsync(keyA, _ => { calls++; return Task.CompletedTask; }, default));
                var fingerprint = HmacAutorizacionGuard.CalcularHuella(keyA);
                // Simulates an explicit administrator action, not runtime provisioning.
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE estado_hmac_autorizacion SET provisionado = true, huella = {fingerprint} WHERE id = 1
                    """);
                Assert.Equal(EstadoHmacAutorizacion.Disponible,
                    await guard.EjecutarAsync(keyA, _ => { calls++; return Task.CompletedTask; }, default));
                await Assert.ThrowsAsync<InvalidOperationException>(() => guard.EjecutarAsync(keyA, async ct =>
                {
                    await context.Database.ExecuteSqlRawAsync("""
                        INSERT INTO limite_autorizacion (id_usuario_solicita, hash_nombre, intentos_fallidos, ultima_actividad_utc)
                        VALUES (1, repeat('d', 64), 1, now())
                        """, ct);
                    throw new InvalidOperationException("Fallo ficticio.");
                }, default));
                Assert.Empty(await context.LimitesAutorizacion.ToListAsync());
                Assert.Equal(EstadoHmacAutorizacion.Espera,
                    await guard.EjecutarAsync(keyB, _ => { calls++; return Task.CompletedTask; }, default));
            }
            await using (var context = database.CreateContext())
            {
                var guard = new HmacAutorizacionGuard(context, clock);
                clock.Now = clock.Now.AddHours(23);
                Assert.Equal(EstadoHmacAutorizacion.Espera,
                    await guard.EjecutarAsync(keyB, _ => { calls++; return Task.CompletedTask; }, default));
                // A different key during the waiting period restarts the barrier.
                Assert.Equal(EstadoHmacAutorizacion.Espera,
                    await guard.EjecutarAsync(keyA, _ => { calls++; return Task.CompletedTask; }, default));
                Assert.Equal(EstadoHmacAutorizacion.Espera,
                    await guard.EjecutarAsync(keyB, _ => { calls++; return Task.CompletedTask; }, default));
                clock.Now = clock.Now.AddHours(1);
                Assert.Equal(EstadoHmacAutorizacion.Espera,
                    await guard.EjecutarAsync(keyB, _ => { calls++; return Task.CompletedTask; }, default));
                clock.Now = clock.Now.AddHours(23);
                Assert.Equal(EstadoHmacAutorizacion.Disponible,
                    await guard.EjecutarAsync(keyB, _ => { calls++; return Task.CompletedTask; }, default));
                Assert.Equal(2, calls);
                await context.Database.ExecuteSqlRawAsync("DELETE FROM estado_hmac_autorizacion WHERE id = 1");
                Assert.Equal(EstadoHmacAutorizacion.MetadatosAusentes,
                    await guard.EjecutarAsync(keyB, _ => { calls++; return Task.CompletedTask; }, default));
                Assert.Equal(2, calls);
            }
        }
        finally { await database.DisposeAsync(); }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Hmac_guard_serializes_key_change_with_inflight_work()
    {
        var database = new TemporaryPostgresDatabase();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await database.InitializeAsync();
            var keyA = Enumerable.Repeat((byte)1, 32).ToArray();
            var keyB = Enumerable.Repeat((byte)2, 32).ToArray();
            var fingerprint = HmacAutorizacionGuard.CalcularHuella(keyA);
            await using var first = database.CreateContext();
            await using var second = database.CreateContext();
            await using var observer = new NpgsqlConnection(database.ConnectionString);
            await observer.OpenAsync();
            await first.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE estado_hmac_autorizacion SET provisionado = true, huella = {fingerprint} WHERE id = 1
                """);
            await second.Database.OpenConnectionAsync();
            var secondPid = ((NpgsqlConnection)second.Database.GetDbConnection()).ProcessID;
            var clock = new TestClock { Now = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero) };
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstTask = new HmacAutorizacionGuard(first, clock).EjecutarAsync(keyA, async ct =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(ct);
            }, default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var secondTask = new HmacAutorizacionGuard(second, clock).EjecutarAsync(keyB, _ => Task.CompletedTask, default);
            var blocked = false;
            for (var attempt = 0; attempt < 100 && !blocked; attempt++)
            {
                await using var command = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@pid)) > 0", observer);
                command.Parameters.AddWithValue("pid", secondPid);
                blocked = (bool)(await command.ExecuteScalarAsync())!;
                if (!blocked) await Task.Delay(50);
            }
            Assert.True(blocked);
            release.SetResult();
            Assert.Equal(EstadoHmacAutorizacion.Disponible, await firstTask.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(EstadoHmacAutorizacion.Espera, await secondTask.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            release.TrySetResult();
            await database.DisposeAsync();
        }
    }

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
                INSERT INTO grupo_mesa (id_grupo, id_usuario_creacion, fecha_creacion, activo)
                VALUES (103, 1, now(), true);
                UPDATE mesa SET id_grupo = 103, estado = 'Agrupada' WHERE id_mesa IN (3, 4);
                INSERT INTO grupo_mesa_miembro (id_grupo, id_mesa, fecha_asignacion)
                VALUES (103, 3, now()), (103, 4, now());
                INSERT INTO pedido (id_pedido, id_grupo, id_usuario, fecha_hora, subtotal, impuestos, total, estado)
                VALUES (103, 103, 1, now(), 10, 0, 10, 'Abierto');
                INSERT INTO comanda (id_comanda, id_pedido, id_usuario, fecha_hora, estado)
                VALUES (103, 103, 1, now(), 'Abierta');
                INSERT INTO detalle_pedido (id_detalle, id_comanda, id_producto, nombre_producto, cantidad, precio_unitario, subtotal, notas)
                VALUES (103, 103, 101, 'Plato histórico', 1, 10, 10, '');
                INSERT INTO receta (id_receta, id_producto, activa, fecha_actualizacion_utc)
                VALUES (103, 101, true, now());
                INSERT INTO detalle_receta (id_receta, id_ingrediente, cantidad_requerida) VALUES (103, 101, 2);
                """);
            await context.Database.MigrateAsync();
            Assert.Equal(1, await context.Pedidos.CountAsync(x => x.IdPedido == 101 && x.Total == 10));
            Assert.Equal(1, await context.Comandas.CountAsync(x => x.IdComanda == 101 && x.RevisionDetalles == 1));
            Assert.Equal(1, await context.DetallesPedido.CountAsync(x => x.IdDetalle == 101 && !x.Cancelado));
            Assert.Equal(1, await context.MovimientosInventario.CountAsync(x => x.IdComanda == 101 && x.IdAnulacion == null));
            Assert.Equal(1, await context.Cobros.CountAsync(x => x.IdCobro == 101 && x.IdPedido == 101 && x.Total == 10));
            Assert.Equal(1, await context.ComprobantesCobroLineas.CountAsync(x => x.IdCobro == 101 && x.IdDetalleOrigen == 101 && x.Subtotal == 10));
            Assert.Equal(1, await context.Pedidos.CountAsync(x => x.IdPedido == 103 && x.IdGrupo == 103 && x.Estado == "Abierto"));
            Assert.Equal(2, await context.GruposMesaMiembros.CountAsync(x => x.IdGrupo == 103));
            Assert.Equal(1, await context.DetallesReceta.CountAsync(x => x.IdReceta == 103 && x.CantidadRequerida == 2));
            Assert.Equal(0, await context.MovimientosInventario.CountAsync(x => x.IdComanda == 103));
            Assert.Empty(await context.ConsumosDetalle.ToListAsync());
            Assert.Empty(await context.AnulacionesPedido.ToListAsync());
            Assert.Equal(1, await context.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM estado_hmac_autorizacion WHERE id = 1 AND NOT provisionado AND huella IS NULL").SingleAsync());
            var unprovisioned = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                UPDATE estado_hmac_autorizacion SET huella = repeat('a', 64) WHERE id = 1
                """));
            Assert.Equal(PostgresErrorCodes.CheckViolation, unprovisioned.SqlState);
            Assert.True(await context.Database.SqlQueryRaw<bool>("SELECT 'NaN'::numeric > 0 AS \"Value\"").SingleAsync());
            var invalidQuantity = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO consumo_detalle (id_detalle, id_ingrediente, cantidad) VALUES (101, 101, 'NaN')
                """));
            Assert.Equal(PostgresErrorCodes.CheckViolation, invalidQuantity.SqlState);
            var invalidAmount = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO anulacion_pedido (id_pedido, id_comanda, id_detalle, cantidad_anulada, monto_revertido,
                    estado_comanda_previo, id_usuario_solicita, id_usuario_autoriza, motivo, inventario_revertido, fecha_utc)
                VALUES (101, 101, 101, 1, 'NaN', 'Lista', 1, 1, 'Prueba ficticia', true, now())
                """));
            Assert.Equal(PostgresErrorCodes.CheckViolation, invalidAmount.SqlState);
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO pedido (id_pedido, id_mesa, id_usuario, fecha_hora, subtotal, impuestos, total, estado)
                VALUES (102, 2, 1, now(), 10, 0, 10, 'Abierto');
                INSERT INTO comanda (id_comanda, id_pedido, id_usuario, fecha_hora, estado)
                VALUES (102, 102, 1, now(), 'Abierta');
                INSERT INTO detalle_pedido (id_detalle, id_comanda, id_producto, nombre_producto, cantidad, precio_unitario, subtotal, notas)
                VALUES (102, 102, 101, 'Plato histórico', 1, 10, 10, '');
                """);
            foreach (var (pedido, comanda, detalle) in new[] { (102, 101, 101), (101, 101, 102) })
            {
                var mismatch = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO anulacion_pedido (id_pedido, id_comanda, id_detalle, cantidad_anulada, monto_revertido,
                        estado_comanda_previo, id_usuario_solicita, id_usuario_autoriza, motivo, inventario_revertido, fecha_utc)
                    VALUES ({pedido}, {comanda}, {detalle}, 1, 10, 'Lista', 1, 1, 'Prueba ficticia', true, now())
                    """));
                Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, mismatch.SqlState);
            }
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO anulacion_pedido (id_anulacion, id_pedido, id_comanda, id_detalle, cantidad_anulada, monto_revertido,
                    estado_comanda_previo, id_usuario_solicita, id_usuario_autoriza, motivo, inventario_revertido, fecha_utc)
                VALUES (201, 101, 101, 101, 1, 10, 'Lista', 1, 1, 'Prueba ficticia', true, now())
                """);
            var reversalMismatch = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                INSERT INTO movimiento_inventario
                    (id_ingrediente, tipo_movimiento, cantidad_delta, stock_anterior, stock_posterior,
                     id_pedido, id_comanda, id_anulacion, id_usuario, motivo, fecha_utc)
                VALUES (101, 'ReversionCancelacion', 1, 9, 10, 102, 102, 201, 1, 'Prueba ficticia', now())
                """));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, reversalMismatch.SqlState);
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
            foreach (var (status, code, json, terminal) in new[]
            {
                (201, "OK", "null", "now()"),
                (500, "OK", "{}", "now()"),
                (201, " ", "{}", "now()"),
                (201, "OK", "{}", "now() - interval '1 minute'")
            })
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO operacion_anulacion
                        (id_operacion, id_usuario_propietario, hash_intencion, estado, fecha_creacion_utc,
                         fecha_terminal_utc, status_resultado, codigo_resultado, resultado_json)
                    VALUES (gen_random_uuid(), 1, repeat('a', 64), 'Completada', now(),
                        CASE WHEN {terminal} = 'now()' THEN now() ELSE now() - interval '1 minute' END,
                        {status}, {code}, CAST({json} AS jsonb))
                    """));
                Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            }
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO operacion_anulacion
                    (id_operacion, id_usuario_propietario, estado, fecha_creacion_utc,
                     fecha_terminal_utc, status_resultado, codigo_resultado, resultado_json)
                VALUES (gen_random_uuid(), 1, 'Cancelada', now(), now(), 200, 'BARRERA', '{{}}')
                """);
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
            await context.Database.ExecuteSqlRawAsync("""
                UPDATE autorizacion_anulacion SET fecha_consumo_utc = now()
                WHERE id_operacion = '00000000-0000-4000-8000-000000000202';
                UPDATE operacion_anulacion SET estado = 'Completada', fecha_terminal_utc = now(),
                    status_resultado = 201, codigo_resultado = 'OK', resultado_json = '{{}}'
                WHERE id_operacion = '00000000-0000-4000-8000-000000000202';
                """);
            var tokenError = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                UPDATE autorizacion_anulacion SET fecha_consumo_utc = NULL
                WHERE id_operacion = '00000000-0000-4000-8000-000000000202'
                """));
            Assert.Equal(PostgresErrorCodes.CheckViolation, tokenError.SqlState);
            var terminalError = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
                UPDATE operacion_anulacion SET estado = 'Cancelada', status_resultado = 200
                WHERE id_operacion = '00000000-0000-4000-8000-000000000202'
                """));
            Assert.Equal(PostgresErrorCodes.CheckViolation, terminalError.SqlState);
        }
        finally { await database.DisposeAsync(); }
    }
}
