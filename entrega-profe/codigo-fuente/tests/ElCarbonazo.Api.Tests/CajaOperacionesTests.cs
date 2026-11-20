using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class CajaOperacionesTests
{
    [Fact]
    public async Task Reconciliation_evidence_distinguishes_terminal_intent_and_tombstone()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var context = db.CreateContext();
            await context.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
            var audit = Audit(context);
            var operations = new CajaOperacionesService(context, audit, TimeProvider.System);
            var cancellations = new CajaCancelacionService(context, audit, TimeProvider.System);
            var intent = Guid.NewGuid();
            Assert.Equal(200, (await operations.PrepararAsync(intent, 1, CajaOperacionCanonica.Abrir(0, true), default)).Status);
            Assert.Equal(409, (await operations.EvidenciaAsync(intent, 1, default)).Error.Status);
            Assert.Equal(200, (await cancellations.CancelarAsync(intent, 1, false, default)).Status);
            var evidence = (await operations.EvidenciaAsync(intent, 1, default)).Evidencia;
            Assert.NotNull(evidence);
            Assert.Equal("AbrirTurno", evidence.Tipo);
            Assert.False(evidence.SinIntencion);
            Assert.Null(evidence.IdPedido);
            var barrier = Guid.NewGuid();
            await cancellations.CancelarAsync(barrier, 1, false, default);
            var tombstone = (await operations.EvidenciaAsync(barrier, 1, default)).Evidencia;
            Assert.NotNull(tombstone);
            Assert.True(tombstone.SinIntencion);
            Assert.Null(tombstone.Tipo);
            Assert.Equal(404, (await operations.EvidenciaAsync(Guid.NewGuid(), 1, default)).Error.Status);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Payment_audit_failure_rolls_back_payment_snapshot_and_order()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            var paymentId = Guid.NewGuid();
            int orderId;
            await using (var setup = db.CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
                (await setup.Mesas.SingleAsync(x => x.IdMesa == 1)).Estado = MesaStates.Ocupada;
                var product = new Producto { Nombre = "Prueba", Categoria = "Comida", Precio = 10m, Disponible = true };
                var order = new Pedido
                {
                    IdMesa = 1,
                    IdUsuario = 1,
                    FechaHora = DateTimeOffset.UtcNow,
                    Estado = PedidoStates.Listo,
                    Subtotal = 10m,
                    Total = 10m
                };
                setup.Productos.Add(product);
                setup.Pedidos.Add(order);
                await setup.SaveChangesAsync();
                orderId = order.IdPedido;
                var command = new Comanda { IdPedido = order.IdPedido, IdUsuario = 1, FechaHora = DateTimeOffset.UtcNow, Estado = "Lista" };
                command.Detalles.Add(new DetallePedido
                {
                    IdProducto = product.IdProducto,
                    NombreProducto = product.Nombre,
                    Cantidad = 1,
                    PrecioUnitario = 10m,
                    Subtotal = 10m,
                    Notas = ""
                });
                setup.Comandas.Add(command);
                await setup.SaveChangesAsync();
                var audit = Audit(setup);
                var opening = Guid.NewGuid();
                var service = new CajaOperacionesService(setup, audit, TimeProvider.System, new CajaAperturaExecutor(setup, audit, TimeProvider.System));
                await service.PrepararAsync(opening, 1, CajaOperacionCanonica.Abrir(0, true), default);
                Assert.Equal(201, (await service.EjecutarAsync(opening, 1, default)).Status);
                var shift = await setup.TurnosCaja.Select(x => x.IdTurnoCaja).SingleAsync();
                var loaded = await setup.Pedidos.AsNoTracking().Include(x => x.Comandas).ThenInclude(x => x.Detalles).SingleAsync(x => x.IdPedido == orderId);
                var hash = CajaElegibilidad.Evaluar(loaded).HuellaLineas;
                var intent = CajaOperacionCanonica.Cobrar(orderId, shift, 10, hash, CajaStates.Efectivo, 10, null, false, false);
                Assert.Equal(200, (await service.PrepararAsync(paymentId, 1, intent, default)).Status);
            }
            await using (var context = db.CreateContext())
            {
                var audit = new FailingAudit();
                var service = new CajaOperacionesService(context, audit, TimeProvider.System, new CajaCobroExecutor(context, audit, TimeProvider.System));
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.EjecutarAsync(paymentId, 1, default));
            }
            await using var verify = db.CreateContext();
            Assert.Equal(PedidoStates.Listo, await verify.Pedidos.Where(x => x.IdPedido == orderId).Select(x => x.Estado).SingleAsync());
            Assert.Empty(await verify.Cobros.ToListAsync());
            Assert.Empty(await verify.ComprobantesCobroLineas.ToListAsync());
            Assert.Equal(CajaStates.OperacionPreparada, await verify.OperacionesCaja.Where(x => x.IdOperacion == paymentId).Select(x => x.Estado).SingleAsync());
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Closing_totals_use_confirmed_payments_per_method_and_cash_net_of_change()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            var closing = Guid.NewGuid();
            int shiftId;
            await using (var setup = db.CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
                var audit = Audit(setup);
                var service = new CajaOperacionesService(setup, audit, TimeProvider.System, new CajaAperturaExecutor(setup, audit, TimeProvider.System));
                var opening = Guid.NewGuid();
                await service.PrepararAsync(opening, 1, CajaOperacionCanonica.Abrir(10, true), default);
                Assert.Equal(201, (await service.EjecutarAsync(opening, 1, default)).Status);
                shiftId = await setup.TurnosCaja.Select(x => x.IdTurnoCaja).SingleAsync();
                foreach (var (method, amount) in new[] { (CajaStates.Efectivo, 20m), (CajaStates.Tarjeta, 30m), (CajaStates.Transferencia, 40m) })
                {
                    var order = new Pedido
                    {
                        IdMesa = 1,
                        IdUsuario = 1,
                        FechaHora = DateTimeOffset.UtcNow,
                        Subtotal = amount,
                        Total = amount,
                        Estado = PedidoStates.Pagado
                    };
                    var operation = new OperacionCaja
                    {
                        IdOperacion = Guid.NewGuid(),
                        IdUsuarioPropietario = 1,
                        Tipo = "CobrarCuenta",
                        VersionContrato = 1,
                        Argumentos = "{}",
                        HashArgumentos = new string('a', 64),
                        Estado = CajaStates.OperacionCompletada,
                        FechaCreacion = DateTimeOffset.UtcNow,
                        FechaTerminal = DateTimeOffset.UtcNow,
                        StatusResultado = 200,
                        CodigoResultado = "OK",
                        ResultadoJson = "{}"
                    };
                    setup.Pedidos.Add(order);
                    setup.OperacionesCaja.Add(operation);
                    await setup.SaveChangesAsync();
                    setup.Cobros.Add(new Cobro
                    {
                        IdPedido = order.IdPedido,
                        IdTurnoCaja = shiftId,
                        IdUsuario = 1,
                        IdOperacion = operation.IdOperacion,
                        MetodoPago = method,
                        Subtotal = amount,
                        Total = amount,
                        EfectivoRecibido = method == CajaStates.Efectivo ? amount + 5 : null,
                        Vuelto = method == CajaStates.Efectivo ? 5 : null,
                        ReferenciaExterna = method == CajaStates.Efectivo ? null : "ref-" + method,
                        VerificacionExternaConfirmada = method != CajaStates.Efectivo,
                        FechaHora = DateTimeOffset.UtcNow
                    });
                    await setup.SaveChangesAsync();
                }
                await service.PrepararAsync(closing, 1, CajaOperacionCanonica.Cerrar(shiftId, 30, null, true), default);
            }
            await using (var context = db.CreateContext())
            {
                var audit = Audit(context);
                var result = await new CajaOperacionesService(context, audit, TimeProvider.System, new CajaCierreExecutor(context, audit, TimeProvider.System))
                    .EjecutarAsync(closing, 1, default);
                Assert.Equal(200, result.Status);
            }
            await using var verify = db.CreateContext();
            var shift = await verify.TurnosCaja.SingleAsync(x => x.IdTurnoCaja == shiftId);
            Assert.Equal(30m, shift.EfectivoEsperado);
            Assert.Equal(20m, shift.TotalEfectivo);
            Assert.Equal(30m, shift.TotalTarjeta);
            Assert.Equal(40m, shift.TotalTransferencia);
            Assert.Equal(0m, shift.Diferencia);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Closing_audit_failure_rolls_back_shift_and_keeps_operation_prepared()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            var opening = Guid.NewGuid();
            var closing = Guid.NewGuid();
            int shiftId;
            await using (var setup = db.CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
                var audit = Audit(setup);
                var service = new CajaOperacionesService(setup, audit, TimeProvider.System, new CajaAperturaExecutor(setup, audit, TimeProvider.System));
                await service.PrepararAsync(opening, 1, CajaOperacionCanonica.Abrir(10, true), default);
                Assert.Equal(201, (await service.EjecutarAsync(opening, 1, default)).Status);
                shiftId = await setup.TurnosCaja.Select(x => x.IdTurnoCaja).SingleAsync();
                await service.PrepararAsync(closing, 1, CajaOperacionCanonica.Cerrar(shiftId, 10, null, true), default);
            }
            await using (var context = db.CreateContext())
            {
                var audit = new FailingAudit();
                var service = new CajaOperacionesService(context, audit, TimeProvider.System, new CajaCierreExecutor(context, audit, TimeProvider.System));
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.EjecutarAsync(closing, 1, default));
            }
            await using var verify = db.CreateContext();
            Assert.Equal(CajaStates.TurnoAbierto, await verify.TurnosCaja.Where(x => x.IdTurnoCaja == shiftId).Select(x => x.Estado).SingleAsync());
            Assert.Equal(CajaStates.OperacionPreparada, await verify.OperacionesCaja.Where(x => x.IdOperacion == closing).Select(x => x.Estado).SingleAsync());
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revoked_role_while_waiting_for_global_lock_blocks_prepare_and_execution(bool execute)
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            var id = Guid.NewGuid();
            await using (var setup = db.CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
                if (execute) await CreateService(setup).PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(0, true), default);
            }
            await using var blocker = new NpgsqlConnection(db.ConnectionString);
            await blocker.OpenAsync();
            await using var tx = await blocker.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(40040001)", blocker, tx))
                await command.ExecuteNonQueryAsync();
            var name = $"caja_role_{Guid.NewGuid():N}";
            var options = new DbContextOptionsBuilder<ElCarbonazo.Api.Data.AppDbContext>()
                .UseNpgsql(new NpgsqlConnectionStringBuilder(db.ConnectionString) { ApplicationName = name }.ConnectionString).Options;
            await using var context = new ElCarbonazo.Api.Data.AppDbContext(options);
            var audit = Audit(context);
            var service = new CajaOperacionesService(context, audit, TimeProvider.System, new CajaAperturaExecutor(context, audit, TimeProvider.System));
            var request = execute ? service.EjecutarAsync(id, 1, default) : service.PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(0, true), default);
            var committed = false;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await using var observer = new NpgsqlConnection(db.ConnectionString);
                await observer.OpenAsync(timeout.Token);
                while (true)
                {
                    await using var check = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name = @name AND cardinality(pg_blocking_pids(pid)) > 0)", observer);
                    check.Parameters.AddWithValue("name", name);
                    if ((bool)(await check.ExecuteScalarAsync(timeout.Token))!) break;
                    await Task.Delay(25, timeout.Token);
                }
                await using (var revoke = db.CreateContext())
                    await revoke.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Mesero' WHERE id_usuario = 1");
                await tx.CommitAsync();
                committed = true;
                Assert.Equal(403, (await request.WaitAsync(TimeSpan.FromSeconds(20))).Status);
                await using var verify = db.CreateContext();
                Assert.Empty(await verify.TurnosCaja.ToListAsync());
                Assert.Equal(execute ? 1 : 0, await verify.OperacionesCaja.CountAsync(x => x.IdOperacion == id));
            }
            finally
            {
                if (!committed) await tx.RollbackAsync();
            }
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Opening_audit_failure_rolls_back_shift_and_keeps_operation_prepared()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            var id = Guid.NewGuid();
            await using (var setup = db.CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
                await CreateService(setup).PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(0, true), default);
            }
            await using (var context = db.CreateContext())
            {
                var audit = new FailingAudit();
                var service = new CajaOperacionesService(context, audit, TimeProvider.System, new CajaAperturaExecutor(context, audit, TimeProvider.System));
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.EjecutarAsync(id, 1, default));
            }
            await using var verify = db.CreateContext();
            Assert.Empty(await verify.TurnosCaja.ToListAsync());
            Assert.Equal(CajaStates.OperacionPreparada, await verify.OperacionesCaja.Where(x => x.IdOperacion == id).Select(x => x.Estado).SingleAsync());
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Concurrent_opening_executions_never_create_two_open_shifts()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
            await using (var setup = db.CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
                foreach (var id in ids) await CreateService(setup).PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(1, true), default);
            }
            async Task<CajaCancelacionResult> Run(Guid id)
            {
                await using var context = db.CreateContext();
                var audit = Audit(context);
                return await new CajaOperacionesService(context, audit, TimeProvider.System, new CajaAperturaExecutor(context, audit, TimeProvider.System))
                    .EjecutarAsync(id, 1, default);
            }
            var results = await Task.WhenAll(ids.Select(Run));
            Assert.All(results, result => Assert.True(result.Status is 201 or 409));
            await using var verify = db.CreateContext();
            Assert.True(await verify.TurnosCaja.CountAsync(x => x.Estado == CajaStates.TurnoAbierto) <= 1);
            Assert.True(await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "TurnoCajaAbierto") <= 1);
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData("success", CajaStates.OperacionCompletada, 1)]
    [InlineData("reject", CajaStates.OperacionRechazada, 0)]
    [InlineData("sql-reject", CajaStates.OperacionRechazada, 0)]
    [InlineData("exception", CajaStates.OperacionPreparada, 0)]
    public async Task Execute_commits_or_rolls_back_domain_and_terminal_together(string behavior, string state, int auditCount)
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            var id = Guid.NewGuid();
            await using (var setup = db.CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
                await CreateService(setup).PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(10, true), default);
            }
            await using (var context = db.CreateContext())
            {
                var executor = new FakeExecutor(context, behavior);
                var service = new CajaOperacionesService(context, Audit(context), TimeProvider.System, executor);
                if (behavior == "exception")
                    await Assert.ThrowsAsync<InvalidOperationException>(() => service.EjecutarAsync(id, 1, default));
                else
                {
                    Assert.Equal(state, (await service.EjecutarAsync(id, 1, default)).Estado);
                    Assert.Equal(state, (await service.EjecutarAsync(id, 1, default)).Estado);
                    Assert.Equal(1, executor.Calls);
                }
            }
            await using var verify = db.CreateContext();
            Assert.Equal(state, await verify.OperacionesCaja.Where(x => x.IdOperacion == id).Select(x => x.Estado).SingleAsync());
            Assert.Equal(auditCount, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "PruebaCaja"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Cancellation_before_commit_leaves_operation_prepared_and_can_be_cancelled()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            var id = Guid.NewGuid();
            await using (var setup = db.CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
                await CreateService(setup).PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(10, true), default);
            }
            using var timeout = new CancellationTokenSource();
            var executor = new BlockingExecutor();
            await using (var context = db.CreateContext())
            {
                var running = new CajaOperacionesService(context, Audit(context), TimeProvider.System, executor)
                    .EjecutarAsync(id, 1, timeout.Token);
                await executor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                timeout.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            }
            await using (var verify = db.CreateContext())
            {
                Assert.Equal(CajaStates.OperacionPreparada, await verify.OperacionesCaja.Where(x => x.IdOperacion == id).Select(x => x.Estado).SingleAsync());
                Assert.Empty(await verify.TurnosCaja.ToListAsync());
                Assert.Equal(CajaStates.OperacionCancelada,
                    (await new CajaCancelacionService(verify, Audit(verify), TimeProvider.System).CancelarAsync(id, 1, false, default)).Estado);
            }
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Cancel_started_during_execution_replays_committed_result()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            var id = Guid.NewGuid();
            await using (var setup = db.CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
                await CreateService(setup).PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(10, true), default);
            }
            var executor = new BlockingExecutor();
            await using var executionContext = db.CreateContext();
            var execution = new CajaOperacionesService(executionContext, Audit(executionContext), TimeProvider.System, executor)
                .EjecutarAsync(id, 1, default);
            try
            {
                await executor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                await using var cancellationContext = db.CreateContext();
                var cancellation = new CajaCancelacionService(cancellationContext, Audit(cancellationContext), TimeProvider.System)
                    .CancelarAsync(id, 1, false, default);
                executor.Release.TrySetResult();
                Assert.Equal(CajaStates.OperacionCompletada, (await execution.WaitAsync(TimeSpan.FromSeconds(20))).Estado);
                Assert.Equal(CajaStates.OperacionCompletada, (await cancellation.WaitAsync(TimeSpan.FromSeconds(20))).Estado);
                await using var verify = db.CreateContext();
                Assert.Empty(await verify.EventosAuditoria.Where(x => x.TipoEvento == "OperacionCajaCancelada").ToListAsync());
            }
            finally { executor.Release.TrySetResult(); }
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Payment_preparation_rejects_missing_shift_without_persisting_intent()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var context = db.CreateContext();
            await context.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
            var intent = CajaOperacionCanonica.Cobrar(1, 999, 10, new string('a', 64), CajaStates.Efectivo, 10, null, false, false);
            Assert.Equal("TURNO_NO_ABIERTO", (await CreateService(context).PrepararAsync(Guid.NewGuid(), 1, intent, default)).Code);
            Assert.Empty(await context.OperacionesCaja.ToListAsync());
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Prepare_replays_identical_payload_rejects_changes_and_execute_fails_closed()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var context = db.CreateContext();
            await context.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
            var service = CreateService(context);
            var id = Guid.NewGuid();
            var request = CajaOperacionCanonica.Abrir(10m, true);
            Assert.Equal(CajaStates.OperacionPreparada, (await service.PrepararAsync(id, 1, request, default)).Estado);
            Assert.Equal(CajaStates.OperacionPreparada, (await service.PrepararAsync(id, 1, request, default)).Estado);
            Assert.Equal(409, (await service.PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(11m, true), default)).Status);
            Assert.Equal(CajaStates.OperacionPreparada, (await service.ConsultarAsync(id, 1, default)).Estado);
            Assert.Equal(503, (await service.EjecutarAsync(id, 1, default)).Status);
            Assert.Equal(CajaStates.OperacionPreparada, (await context.OperacionesCaja.AsNoTracking().SingleAsync()).Estado);
            Assert.Empty(await context.TurnosCaja.ToListAsync());
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Concurrent_prepare_and_cancel_never_remove_barrier_and_terminal_replays()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using (var setup = db.CreateContext())
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
            var id = Guid.NewGuid();
            async Task<CajaCancelacionResult> Prepare()
            {
                await using var context = db.CreateContext();
                return await CreateService(context).PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(10, true), default);
            }
            async Task<CajaCancelacionResult> Cancel()
            {
                await using var context = db.CreateContext();
                return await new CajaCancelacionService(context, Audit(context), TimeProvider.System).CancelarAsync(id, 1, false, default);
            }
            var results = await Task.WhenAll(Prepare(), Cancel());
            Assert.Contains(results, x => x.Estado == CajaStates.OperacionCancelada);
            await using var verify = db.CreateContext();
            var operation = await verify.OperacionesCaja.AsNoTracking().SingleAsync(x => x.IdOperacion == id);
            Assert.Equal(CajaStates.OperacionCancelada, operation.Estado);
            Assert.Equal(1, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "OperacionCajaCancelada"));
            var replay = await CreateService(verify).EjecutarAsync(id, 1, default);
            Assert.Equal(CajaStates.OperacionCancelada, replay.Estado);
            Assert.Empty(await verify.TurnosCaja.ToListAsync());
        }
        finally { await db.DisposeAsync(); }
    }

    private static CajaOperacionesService CreateService(ElCarbonazo.Api.Data.AppDbContext context) =>
        new(context, Audit(context), TimeProvider.System);
    private static AuditRecorder Audit(ElCarbonazo.Api.Data.AppDbContext context) =>
        new(context, new HttpContextAccessor(), TimeProvider.System);

    private sealed class FakeExecutor(ElCarbonazo.Api.Data.AppDbContext context, string behavior) : ICajaEjecutorDominio
    {
        public int Calls { get; private set; }
        public async Task<CajaResultadoDominio> EjecutarAsync(CajaEjecucionPreparada operacion, CancellationToken cancellationToken)
        {
            Calls++;
            Audit(context).Record("PruebaCaja", "Exitoso", operacion.IdUsuario);
            await context.SaveChangesAsync(cancellationToken);
            if (behavior == "sql-reject")
            {
                try { await context.Database.ExecuteSqlRawAsync("SELECT 1 / 0", cancellationToken); }
                catch (Npgsql.PostgresException) { return new(false, 409, "CONFLICTO", "{\"ok\":false}"); }
            }
            if (behavior == "exception") throw new InvalidOperationException("Injected domain failure");
            return behavior == "success"
                ? new(true, 200, "OK", "{\"ok\":true}")
                : new(false, 409, "CONFLICTO", "{\"ok\":false}");
        }
    }

    private sealed class BlockingExecutor : ICajaEjecutorDominio
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CajaResultadoDominio> EjecutarAsync(CajaEjecucionPreparada operacion, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(true, 200, "OK", "{}");
        }
    }

    private sealed class FailingAudit : IAuditRecorder
    {
        public void Record(string eventType, string result, int? userId = null, Guid? sessionId = null,
            string? entityType = null, string? entityId = null, IReadOnlyDictionary<string, object?>? details = null) =>
            throw new InvalidOperationException("Injected audit failure");
    }
}
