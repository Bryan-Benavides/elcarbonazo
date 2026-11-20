using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class CajaPersistenceTests
{
    private const string PedidosMigration = "20261106142048_AddPedidosKdsPersistence";

    [Fact]
    public async Task Caja_migration_preserves_representative_orders_groups_commands_and_details()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync(PedidosMigration);
            await using var context = new PedidosHistoricalContext(database.ConnectionString);
            var now = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
            var group = new GrupoMesa { IdUsuarioCreacion = 1, FechaCreacion = now, Activo = true };
            var product = new Producto { Nombre = "Producto histórico", Categoria = "Comida", Precio = 10m, Disponible = true };
            context.GruposMesa.Add(group);
            context.Productos.Add(product);
            await context.SaveChangesAsync();

            var members = await context.Mesas.Where(x => x.IdMesa == 2 || x.IdMesa == 3).ToListAsync();
            foreach (var member in members)
            {
                member.IdGrupo = group.IdGrupo;
                member.Estado = MesaStates.Agrupada;
                context.GruposMesaMiembros.Add(new GrupoMesaMiembro
                {
                    IdGrupo = group.IdGrupo,
                    IdMesa = member.IdMesa,
                    FechaAsignacion = now
                });
            }
            var individual = new Pedido { IdMesa = 1, IdUsuario = 1, FechaHora = now, Subtotal = 30m, Total = 30m, Estado = "Abierto" };
            var grouped = new Pedido { IdGrupo = group.IdGrupo, IdUsuario = 1, FechaHora = now, Subtotal = 10m, Total = 10m, Estado = "En Preparación" };
            var legacy = new Pedido { IdMesa = 4, IdUsuario = 1, FechaHora = now, Estado = "Listo" };
            var paid = new Pedido { IdMesa = 5, IdUsuario = 1, FechaHora = now, Subtotal = 10m, Total = 10m, Estado = "Pagado" };
            var canceled = new Pedido { IdMesa = 6, IdUsuario = 1, FechaHora = now, Subtotal = 10m, Total = 10m, Estado = "Cancelado" };
            context.Pedidos.AddRange(individual, grouped, legacy, paid, canceled);
            await context.SaveChangesAsync();
            foreach (var (order, state) in new[] { (individual, "Abierta"), (individual, "En Preparación"), (individual, "Lista"), (grouped, "En Preparación"), (paid, "Lista"), (canceled, "Abierta") })
            {
                var command = new Comanda { IdPedido = order.IdPedido, IdUsuario = 1, FechaHora = now, Estado = state };
                command.Detalles.Add(new DetallePedido
                {
                    IdProducto = product.IdProducto,
                    NombreProducto = product.Nombre,
                    Cantidad = 1,
                    PrecioUnitario = 10m,
                    Subtotal = 10m,
                    Notas = ""
                });
                context.Comandas.Add(command);
            }
            await context.SaveChangesAsync();
            var ordersBefore = await context.Pedidos.AsNoTracking().OrderBy(x => x.IdPedido)
                .Select(x => new { x.IdPedido, x.IdMesa, x.IdGrupo, x.IdUsuario, x.FechaHora, x.Subtotal, x.Impuestos, x.Total, x.Estado }).ToArrayAsync();
            var commandsBefore = await context.Comandas.AsNoTracking().OrderBy(x => x.IdComanda)
                .Select(x => new { x.IdComanda, x.IdPedido, x.IdUsuario, x.FechaHora, x.Estado }).ToArrayAsync();
            var detailsBefore = await context.DetallesPedido.AsNoTracking().OrderBy(x => x.IdDetalle)
                .Select(x => new { x.IdDetalle, x.IdComanda, x.IdProducto, x.NombreProducto, x.Cantidad, x.PrecioUnitario, x.Subtotal, x.Notas }).ToArrayAsync();
            var membersBefore = await context.GruposMesaMiembros.AsNoTracking().OrderBy(x => x.IdMesa)
                .Select(x => new { x.IdGrupo, x.IdMesa, x.FechaAsignacion }).ToArrayAsync();

            await using (var migrationContext = database.CreateContext())
                await migrationContext.Database.MigrateAsync();
            context.ChangeTracker.Clear();
            Assert.Equal(ordersBefore, await context.Pedidos.AsNoTracking().OrderBy(x => x.IdPedido)
                .Select(x => new { x.IdPedido, x.IdMesa, x.IdGrupo, x.IdUsuario, x.FechaHora, x.Subtotal, x.Impuestos, x.Total, x.Estado }).ToArrayAsync());
            Assert.Equal(commandsBefore, await context.Comandas.AsNoTracking().OrderBy(x => x.IdComanda)
                .Select(x => new { x.IdComanda, x.IdPedido, x.IdUsuario, x.FechaHora, x.Estado }).ToArrayAsync());
            Assert.Equal(detailsBefore, await context.DetallesPedido.AsNoTracking().OrderBy(x => x.IdDetalle)
                .Select(x => new { x.IdDetalle, x.IdComanda, x.IdProducto, x.NombreProducto, x.Cantidad, x.PrecioUnitario, x.Subtotal, x.Notas }).ToArrayAsync());
            Assert.Equal(membersBefore, await context.GruposMesaMiembros.AsNoTracking().OrderBy(x => x.IdMesa)
                .Select(x => new { x.IdGrupo, x.IdMesa, x.FechaAsignacion }).ToArrayAsync());
            Assert.True(await context.GruposMesa.AnyAsync(x => x.IdGrupo == group.IdGrupo && x.Activo));
            Assert.Equal(2, await context.Mesas.CountAsync(x => x.IdGrupo == group.IdGrupo && x.Estado == MesaStates.Agrupada));
            Assert.False(await context.Comandas.AnyAsync(x => x.IdPedido == legacy.IdPedido));
            Assert.Empty(await context.Cobros.ToListAsync());
            Assert.Empty(await context.TurnosCaja.ToListAsync());
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Shared_advisory_barrier_serializes_prepare_and_close_model(bool prepareFirst)
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using (var setup = database.CreateContext())
            {
                var opening = CreatePreparedOperation("AbrirTurno");
                var closing = CreatePreparedOperation("CerrarTurno");
                setup.OperacionesCaja.AddRange(opening, closing);
                await setup.SaveChangesAsync();
                setup.TurnosCaja.Add(new TurnoCaja
                {
                    Estado = CajaStates.TurnoAbierto,
                    FechaApertura = DateTimeOffset.UtcNow,
                    IdUsuarioApertura = 1,
                    FondoInicial = 0,
                    IdOperacionApertura = opening.IdOperacion
                });
                await setup.SaveChangesAsync();
            }

            await using var first = new NpgsqlConnection(database.ConnectionString);
            await using var second = new NpgsqlConnection(database.ConnectionString);
            await first.OpenAsync();
            await second.OpenAsync();
            await using var firstTx = await first.BeginTransactionAsync();
            await using var secondTx = await second.BeginTransactionAsync();
            // Test-only key: the eventual implementation must define its stable namespace.
            const string barrier = "SELECT pg_try_advisory_xact_lock(40040001)";
            await using (var acquire = new NpgsqlCommand(barrier, first, firstTx))
                Assert.Equal(true, await acquire.ExecuteScalarAsync());
            await using (var blocked = new NpgsqlCommand(barrier, second, secondTx))
                Assert.Equal(false, await blocked.ExecuteScalarAsync());

            if (prepareFirst)
            {
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO operacion_caja (id_operacion, id_usuario_propietario, tipo, version_contrato,
                        argumentos, hash_argumentos, estado, fecha_creacion)
                    VALUES (@id, 1, 'CobrarCuenta', 1, '{}', repeat('a', 64), 'Preparada', now())
                    """, first, firstTx);
                insert.Parameters.AddWithValue("id", Guid.NewGuid());
                await insert.ExecuteNonQueryAsync();
            }
            else
            {
                await using var close = new NpgsqlCommand("""
                    UPDATE turno_caja SET estado = 'Cerrado', fecha_cierre = now(), id_usuario_cierre = 1,
                        id_operacion_cierre = (SELECT id_operacion FROM operacion_caja WHERE tipo = 'CerrarTurno'),
                        efectivo_esperado = 0, efectivo_contado = 0, diferencia = 0, total_efectivo = 0,
                        total_tarjeta = 0, total_transferencia = 0, cantidad_cuentas_pendientes = 0
                    """, first, firstTx);
                await close.ExecuteNonQueryAsync();
            }
            await firstTx.CommitAsync();
            await using (var acquire = new NpgsqlCommand(barrier, second, secondTx))
                Assert.Equal(true, await acquire.ExecuteScalarAsync());
            if (prepareFirst)
            {
                await using var pending = new NpgsqlCommand("SELECT count(*) FROM operacion_caja WHERE tipo = 'CobrarCuenta' AND estado = 'Preparada'", second, secondTx);
                Assert.Equal(1L, await pending.ExecuteScalarAsync());
            }
            else
            {
                await using var shift = new NpgsqlCommand("SELECT estado FROM turno_caja FOR UPDATE", second, secondTx);
                Assert.Equal("Cerrado", await shift.ExecuteScalarAsync());
            }
            await secondTx.RollbackAsync();
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task Payload_check_rejects_null_version_and_type()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();
            var operation = CreatePreparedOperation("CobrarCuenta");
            operation.VersionContrato = null;
            context.OperacionesCaja.Add(operation);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal("CK_operacion_caja_payload", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
            context.ChangeTracker.Clear();
            operation = CreatePreparedOperation("CobrarCuenta");
            operation.Tipo = null;
            context.OperacionesCaja.Add(operation);
            error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal("CK_operacion_caja_payload", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task Incompatible_cash_table_aborts_migration_transaction_without_losing_existing_data()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync(PedidosMigration);
            await using (var context = database.CreateContext())
            {
                await context.Database.ExecuteSqlRawAsync("CREATE TABLE public.turno_caja (marker integer PRIMARY KEY); INSERT INTO public.turno_caja VALUES (7)");
                await Assert.ThrowsAsync<PostgresException>(() => context.Database.MigrateAsync());
            }
            await using var verify = database.CreateContext();
            Assert.Equal(7, await verify.Database.SqlQueryRaw<int>("SELECT marker AS \"Value\" FROM public.turno_caja").SingleAsync());
            Assert.False(await verify.Database.SqlQueryRaw<bool>("SELECT to_regclass('public.operacion_caja') IS NOT NULL AS \"Value\"").SingleAsync());
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task Mvcc_check_does_not_block_later_cash_preparation_while_shift_is_locked()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using (var setup = database.CreateContext())
            {
                var opening = CreatePreparedOperation("AbrirTurno");
                setup.OperacionesCaja.Add(opening);
                await setup.SaveChangesAsync();
                setup.TurnosCaja.Add(new TurnoCaja
                {
                    Estado = CajaStates.TurnoAbierto,
                    FechaApertura = DateTimeOffset.UtcNow,
                    IdUsuarioApertura = 1,
                    FondoInicial = 0,
                    IdOperacionApertura = opening.IdOperacion
                });
                await setup.SaveChangesAsync();
            }
            await using var closer = new NpgsqlConnection(database.ConnectionString);
            await closer.OpenAsync();
            await using var transaction = await closer.BeginTransactionAsync();
            await using (var lockShift = new NpgsqlCommand("SELECT id_turno_caja FROM turno_caja FOR UPDATE", closer, transaction))
                await lockShift.ExecuteScalarAsync();
            await using (var check = new NpgsqlCommand("SELECT count(*) FROM operacion_caja WHERE tipo = 'CobrarCuenta' AND estado = 'Preparada'", closer, transaction))
                Assert.Equal(0L, await check.ExecuteScalarAsync());

            await using (var preparer = database.CreateContext())
            {
                preparer.OperacionesCaja.Add(CreatePreparedOperation("CobrarCuenta"));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await preparer.SaveChangesAsync(timeout.Token);
            }
            await using (var checkAgain = new NpgsqlCommand("SELECT count(*) FROM operacion_caja WHERE tipo = 'CobrarCuenta' AND estado = 'Preparada'", closer, transaction))
                Assert.Equal(1L, await checkAgain.ExecuteScalarAsync());
            await transaction.RollbackAsync();
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task Caja_migration_adds_schema_without_changing_legacy_order_rows()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync(PedidosMigration);
            int legacyOrderId;
            await using (var context = database.CreateContext())
            {
                legacyOrderId = await InsertOrderAsync(context, 0m, "Abierto");

                await context.Database.MigrateAsync();

                var row = await context.Pedidos.AsNoTracking().SingleAsync(order => order.IdPedido == legacyOrderId);
                Assert.Equal("Abierto", row.Estado);
                Assert.Equal(0m, row.Total);
                Assert.Equal(1, await context.Pedidos.CountAsync());

                var tables = await context.Database.SqlQueryRaw<string>("""
                    SELECT table_name AS "Value"
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_name IN ('operacion_caja', 'turno_caja', 'cobro', 'comprobante_cobro_linea')
                    ORDER BY table_name
                    """).ToListAsync();
                Assert.Equal(
                    ["cobro", "comprobante_cobro_linea", "operacion_caja", "turno_caja"],
                    tables);
            }
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task Persistence_constraints_enforce_one_open_shift_and_valid_cash_payment_shape()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();

            var openingOperation = CreatePreparedOperation("AbrirTurno");
            context.OperacionesCaja.Add(openingOperation);
            await context.SaveChangesAsync();

            var shift = new TurnoCaja
            {
                Estado = CajaStates.TurnoAbierto,
                FechaApertura = DateTimeOffset.UtcNow,
                IdUsuarioApertura = 1,
                FondoInicial = 0m,
                IdOperacionApertura = openingOperation.IdOperacion
            };
            context.TurnosCaja.Add(shift);
            await context.SaveChangesAsync();
            Assert.True(shift.IdTurnoCaja > 0);

            var duplicateOpenOperation = CreatePreparedOperation("AbrirTurno");
            context.OperacionesCaja.Add(duplicateOpenOperation);
            await context.SaveChangesAsync();

            var duplicateOpenError = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO turno_caja
                        (estado, fecha_apertura, id_usuario_apertura, fondo_inicial, id_operacion_apertura)
                    VALUES
                        ('Abierto', {DateTimeOffset.UtcNow}, 1, 0, {duplicateOpenOperation.IdOperacion})
                    """));
            Assert.Equal("IX_turno_caja_unico_abierto", duplicateOpenError.ConstraintName);

            var closingOperation = CreatePreparedOperation("CerrarTurno");
            context.OperacionesCaja.Add(closingOperation);
            await context.SaveChangesAsync();
            var closeTime = DateTimeOffset.UtcNow;
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE turno_caja
                SET estado = 'Cerrado', fecha_cierre = {closeTime}, id_usuario_cierre = 1,
                    id_operacion_cierre = {closingOperation.IdOperacion}, efectivo_esperado = 0,
                    efectivo_contado = 0, diferencia = 0, total_efectivo = 0, total_tarjeta = 0,
                    total_transferencia = 0, cantidad_cuentas_pendientes = 0
                WHERE id_turno_caja = {shift.IdTurnoCaja}
                """);
            var closedShiftMutation = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE turno_caja SET efectivo_contado = 1
                    WHERE id_turno_caja = {shift.IdTurnoCaja}
                    """));
            Assert.Equal("CK_turno_caja_cierre_inmutable", closedShiftMutation.ConstraintName);

            await using (var reset = database.CreateContext())
            {
                var orderId = await InsertOrderAsync(reset, 10m, "Listo");
                var paymentOperation = CreatePreparedOperation("CobrarCuenta");
                reset.OperacionesCaja.Add(paymentOperation);
                await reset.SaveChangesAsync();

                var invalidPaymentError = await Assert.ThrowsAsync<PostgresException>(() =>
                    reset.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO cobro (
                            numero_comprobante, id_pedido, id_turno_caja, id_usuario, id_operacion,
                            metodo_pago, subtotal, impuestos, total, efectivo_recibido, vuelto,
                            verificacion_externa_confirmada, referencia_repetida_confirmada, fecha_hora)
                        VALUES (
                            nextval(pg_get_serial_sequence('cobro', 'numero_comprobante')),
                            {orderId}, {shift.IdTurnoCaja}, 1, {paymentOperation.IdOperacion},
                            'Efectivo', 10, 0, 10, 9, 0, false, false, {DateTimeOffset.UtcNow})
                        """));
                Assert.Equal("CK_cobro_metodo", invalidPaymentError.ConstraintName);
            }
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    private static async Task<int> InsertOrderAsync(AppDbContext context, decimal total, string state)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            INSERT INTO pedido (id_mesa, id_grupo, id_usuario, fecha_hora, subtotal, impuestos, total, estado)
            VALUES (1, NULL, 1, TIMESTAMPTZ '2026-11-09 12:00:00+00', @total, 0, @total, @state)
            RETURNING id_pedido
            """;
        var totalParameter = command.CreateParameter();
        totalParameter.ParameterName = "total";
        totalParameter.Value = total;
        command.Parameters.Add(totalParameter);
        var stateParameter = command.CreateParameter();
        stateParameter.ParameterName = "state";
        stateParameter.Value = state;
        command.Parameters.Add(stateParameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static OperacionCaja CreatePreparedOperation(string type) => new()
    {
        IdOperacion = Guid.NewGuid(),
        IdUsuarioPropietario = 1,
        Tipo = type,
        VersionContrato = 1,
        Argumentos = "{}",
        HashArgumentos = new string('a', 64),
        Estado = CajaStates.OperacionPreparada,
        FechaCreacion = DateTimeOffset.UtcNow
    };
}
