using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ElCarbonazo.Api.Tests;

public sealed class CajaCierreCarrerasTests
{
    [Fact]
    public async Task Concurrent_execution_of_same_uuid_replays_one_real_opening()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            var id = Guid.NewGuid();
            await using (var setup = database.CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
                Assert.Equal(CajaStates.OperacionPreparada, (await new CajaOperacionesService(setup, Audit(setup), TimeProvider.System)
                    .PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(10m, true), default)).Estado);
            }
            await using var blocker = new NpgsqlConnection(database.ConnectionString);
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand("SELECT id_operacion FROM operacion_caja WHERE id_operacion = @id FOR UPDATE", blocker, transaction))
            {
                command.Parameters.AddWithValue("id", id);
                await command.ExecuteScalarAsync();
            }
            var names = new[] { $"caja_exec_{Guid.NewGuid():N}", $"caja_exec_{Guid.NewGuid():N}" };
            async Task<CajaCancelacionResult> Execute(string name)
            {
                await using var context = NamedContext(database.ConnectionString, name);
                var audit = Audit(context);
                return await new CajaOperacionesService(context, audit, TimeProvider.System, new CajaAperturaExecutor(context, audit, TimeProvider.System))
                    .EjecutarAsync(id, 1, default);
            }
            var first = Execute(names[0]);
            await WaitBlocked(database.ConnectionString, names[0]);
            var second = Execute(names[1]);
            await WaitBlocked(database.ConnectionString, names[1]);
            await transaction.CommitAsync();
            var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.All(results, result => Assert.Equal(CajaStates.OperacionCompletada, result.Estado));
            using var firstResult = JsonDocument.Parse(results[0].ResultadoJson!);
            using var secondResult = JsonDocument.Parse(results[1].ResultadoJson!);
            Assert.True(JsonElement.DeepEquals(firstResult.RootElement, secondResult.RootElement));
            await using var verify = database.CreateContext();
            Assert.Equal(1, await verify.TurnosCaja.CountAsync());
            Assert.Equal(1, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "TurnoCajaAbierto"));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Concurrent_terminals_cannot_replace_another_actors_uuid_or_change_its_arguments()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            int otherActor;
            await using (var setup = database.CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
                await setup.Database.ExecuteSqlRawAsync("SELECT setval(pg_get_serial_sequence('usuario', 'id_usuario'), (SELECT max(id_usuario) FROM usuario))");
                var actor = new Usuario { Nombre = "Caja prueba", Email = "caja-prueba@example.invalid", ContrasenaHash = "test", Rol = UserRoles.EncargadoCaja, Activo = true };
                setup.Usuarios.Add(actor);
                await setup.SaveChangesAsync();
                otherActor = actor.IdUsuario;
            }
            var id = Guid.NewGuid();
            await using var blocker = new NpgsqlConnection(database.ConnectionString);
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(40040001)", blocker, transaction))
                await command.ExecuteNonQueryAsync();
            var ownerName = $"caja_owner_{Guid.NewGuid():N}";
            var changedName = $"caja_changed_{Guid.NewGuid():N}";
            var otherName = $"caja_other_{Guid.NewGuid():N}";
            async Task<CajaCancelacionResult> Prepare(string name, int actor, decimal amount)
            {
                await using var context = NamedContext(database.ConnectionString, name);
                return await new CajaOperacionesService(context, Audit(context), TimeProvider.System)
                    .PrepararAsync(id, actor, CajaOperacionCanonica.Abrir(amount, true), default);
            }
            var owner = Prepare(ownerName, 1, 10m);
            await WaitBlocked(database.ConnectionString, ownerName);
            var changed = Prepare(changedName, 1, 11m);
            var other = Prepare(otherName, otherActor, 10m);
            await WaitBlocked(database.ConnectionString, changedName);
            await WaitBlocked(database.ConnectionString, otherName);
            await transaction.CommitAsync();
            Assert.Equal(CajaStates.OperacionPreparada, (await owner.WaitAsync(TimeSpan.FromSeconds(20))).Estado);
            Assert.Equal("OPERACION_CLAVE_EN_USO", (await changed.WaitAsync(TimeSpan.FromSeconds(20))).Code);
            Assert.Equal(404, (await other.WaitAsync(TimeSpan.FromSeconds(20))).Status);
            await using var verify = database.CreateContext();
            var operation = await verify.OperacionesCaja.AsNoTracking().SingleAsync(x => x.IdOperacion == id);
            Assert.Equal(1, operation.IdUsuarioPropietario);
            Assert.Equal(CajaOperacionCanonica.Abrir(10m, true)!.HashArgumentos, operation.HashArgumentos);
            Assert.Empty(await verify.TurnosCaja.ToListAsync());
        }
        finally { await database.DisposeAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Prepare_and_cancel_same_uuid_serialize_in_both_orders(bool prepareFirst)
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            var id = Guid.NewGuid();
            await using (var setup = database.CreateContext())
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
            await using var blocker = new NpgsqlConnection(database.ConnectionString);
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand(prepareFirst
                ? "SELECT pg_advisory_xact_lock(40040001)"
                : "LOCK TABLE evento_auditoria IN SHARE MODE", blocker, transaction))
                await command.ExecuteNonQueryAsync();
            var prepareName = $"caja_prepare_{Guid.NewGuid():N}";
            var cancelName = $"caja_cancel_{Guid.NewGuid():N}";
            async Task<CajaCancelacionResult> Prepare()
            {
                await using var context = NamedContext(database.ConnectionString, prepareName);
                return await new CajaOperacionesService(context, Audit(context), TimeProvider.System)
                    .PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(10, true), default);
            }
            async Task<CajaCancelacionResult> Cancel()
            {
                await using var context = NamedContext(database.ConnectionString, cancelName);
                return await new CajaCancelacionService(context, Audit(context), TimeProvider.System).CancelarAsync(id, 1, false, default);
            }
            Task<CajaCancelacionResult> prepare;
            Task<CajaCancelacionResult> cancel;
            if (prepareFirst)
            {
                prepare = Prepare();
                await WaitBlocked(database.ConnectionString, prepareName);
                cancel = Cancel();
                await WaitBlocked(database.ConnectionString, cancelName);
            }
            else
            {
                cancel = Cancel();
                await WaitBlocked(database.ConnectionString, cancelName);
                prepare = Prepare();
                await WaitBlocked(database.ConnectionString, prepareName);
            }
            await transaction.CommitAsync();
            var prepared = await prepare.WaitAsync(TimeSpan.FromSeconds(20));
            var cancelled = await cancel.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(prepareFirst ? CajaStates.OperacionPreparada : null, prepared.Estado);
            Assert.Equal(prepareFirst ? 200 : 409, prepared.Status);
            Assert.Equal(CajaStates.OperacionCancelada, cancelled.Estado);
            await using var verify = database.CreateContext();
            var operation = await verify.OperacionesCaja.AsNoTracking().SingleAsync(x => x.IdOperacion == id);
            Assert.Equal(CajaStates.OperacionCancelada, operation.Estado);
            Assert.Equal(prepareFirst ? "AbrirTurno" : null, operation.Tipo);
            Assert.Equal(1, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "OperacionCajaCancelada"));
            var audit = Audit(verify);
            var executor = new CajaOperacionesService(verify, audit, TimeProvider.System, new CajaAperturaExecutor(verify, audit, TimeProvider.System));
            Assert.Equal(CajaStates.OperacionCancelada, (await executor.EjecutarAsync(id, 1, default)).Estado);
            Assert.Empty(await verify.TurnosCaja.ToListAsync());
        }
        finally { await database.DisposeAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Real_payment_and_cancellation_serialize_in_both_orders(bool executionFirst)
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            var id = Guid.NewGuid();
            int orderId;
            await using (var setup = database.CreateContext())
            {
                (await setup.Mesas.SingleAsync(x => x.IdMesa == 1)).Estado = MesaStates.Ocupada;
                var product = new Producto { Nombre = "Prueba", Categoria = "Comida", Precio = 10m, Disponible = true };
                var order = new Pedido { IdMesa = 1, IdUsuario = 1, FechaHora = DateTimeOffset.UtcNow, Estado = PedidoStates.Listo, Subtotal = 10m, Total = 10m };
                setup.Productos.Add(product);
                setup.Pedidos.Add(order);
                await setup.SaveChangesAsync();
                orderId = order.IdPedido;
                var command = new Comanda { IdPedido = orderId, IdUsuario = 1, FechaHora = DateTimeOffset.UtcNow, Estado = "Lista" };
                command.Detalles.Add(new DetallePedido { IdProducto = product.IdProducto, NombreProducto = product.Nombre, Cantidad = 1, PrecioUnitario = 10m, Subtotal = 10m, Notas = "" });
                setup.Comandas.Add(command);
                await setup.SaveChangesAsync();
                var audit = Audit(setup);
                var service = new CajaOperacionesService(setup, audit, TimeProvider.System, new CajaAperturaExecutor(setup, audit, TimeProvider.System));
                var opening = Guid.NewGuid();
                Assert.Equal(200, (await service.PrepararAsync(opening, 1, CajaOperacionCanonica.Abrir(0, true), default)).Status);
                Assert.Equal(201, (await service.EjecutarAsync(opening, 1, default)).Status);
                var shiftId = await setup.TurnosCaja.Select(x => x.IdTurnoCaja).SingleAsync();
                var loaded = await setup.Pedidos.AsNoTracking().Include(x => x.Comandas).ThenInclude(x => x.Detalles).SingleAsync(x => x.IdPedido == orderId);
                var intent = CajaOperacionCanonica.Cobrar(orderId, shiftId, 10m, CajaElegibilidad.Evaluar(loaded).HuellaLineas, CajaStates.Efectivo, 10m, null, false, false);
                Assert.Equal(CajaStates.OperacionPreparada, (await service.PrepararAsync(id, 1, intent, default)).Estado);
            }
            await using var blocker = new NpgsqlConnection(database.ConnectionString);
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand("SELECT id_operacion FROM operacion_caja WHERE id_operacion = @id FOR UPDATE", blocker, transaction))
            {
                command.Parameters.AddWithValue("id", id);
                await command.ExecuteScalarAsync();
            }
            var executeName = $"caja_payment_{Guid.NewGuid():N}";
            var cancelName = $"caja_cancel_{Guid.NewGuid():N}";
            async Task<CajaCancelacionResult> Execute()
            {
                await using var context = NamedContext(database.ConnectionString, executeName);
                var audit = Audit(context);
                return await new CajaOperacionesService(context, audit, TimeProvider.System, new CajaCobroExecutor(context, audit, TimeProvider.System)).EjecutarAsync(id, 1, default);
            }
            async Task<CajaCancelacionResult> Cancel()
            {
                await using var context = NamedContext(database.ConnectionString, cancelName);
                return await new CajaCancelacionService(context, Audit(context), TimeProvider.System).CancelarAsync(id, 1, false, default);
            }
            Task<CajaCancelacionResult> execution;
            Task<CajaCancelacionResult> cancellation;
            if (executionFirst)
            {
                execution = Execute();
                await WaitBlocked(database.ConnectionString, executeName);
                cancellation = Cancel();
                await WaitBlocked(database.ConnectionString, cancelName);
            }
            else
            {
                cancellation = Cancel();
                await WaitBlocked(database.ConnectionString, cancelName);
                execution = Execute();
                await WaitBlocked(database.ConnectionString, executeName);
            }
            await transaction.CommitAsync();
            var expected = executionFirst ? CajaStates.OperacionCompletada : CajaStates.OperacionCancelada;
            Assert.Equal(expected, (await execution.WaitAsync(TimeSpan.FromSeconds(20))).Estado);
            Assert.Equal(expected, (await cancellation.WaitAsync(TimeSpan.FromSeconds(20))).Estado);
            await using var verify = database.CreateContext();
            Assert.Equal(expected, await verify.OperacionesCaja.Where(x => x.IdOperacion == id).Select(x => x.Estado).SingleAsync());
            Assert.Equal(executionFirst ? PedidoStates.Pagado : PedidoStates.Listo, await verify.Pedidos.Where(x => x.IdPedido == orderId).Select(x => x.Estado).SingleAsync());
            Assert.Equal(executionFirst ? 1 : 0, await verify.Cobros.CountAsync(x => x.IdPedido == orderId));
            Assert.Equal(executionFirst ? 1 : 0, await verify.ComprobantesCobroLineas.CountAsync());
            Assert.Equal(executionFirst ? 1 : 0, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "CobroRegistrado"));
            Assert.Equal(executionFirst ? 0 : 1, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "OperacionCajaCancelada"));
        }
        finally { await database.DisposeAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Real_opening_and_cancellation_serialize_in_both_orders(bool executionFirst)
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            var id = Guid.NewGuid();
            await using (var setup = database.CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
                var audit = Audit(setup);
                var service = new CajaOperacionesService(setup, audit, TimeProvider.System, new CajaAperturaExecutor(setup, audit, TimeProvider.System));
                Assert.Equal(CajaStates.OperacionPreparada, (await service.PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(10m, true), default)).Estado);
            }
            await using var blocker = new NpgsqlConnection(database.ConnectionString);
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand("SELECT id_operacion FROM operacion_caja WHERE id_operacion = @id FOR UPDATE", blocker, transaction))
            {
                command.Parameters.AddWithValue("id", id);
                await command.ExecuteScalarAsync();
            }
            var executeName = $"caja_execute_{Guid.NewGuid():N}";
            var cancelName = $"caja_cancel_{Guid.NewGuid():N}";
            async Task<CajaCancelacionResult> Execute()
            {
                await using var context = NamedContext(database.ConnectionString, executeName);
                var audit = Audit(context);
                return await new CajaOperacionesService(context, audit, TimeProvider.System, new CajaAperturaExecutor(context, audit, TimeProvider.System))
                    .EjecutarAsync(id, 1, default);
            }
            async Task<CajaCancelacionResult> Cancel()
            {
                await using var context = NamedContext(database.ConnectionString, cancelName);
                return await new CajaCancelacionService(context, Audit(context), TimeProvider.System).CancelarAsync(id, 1, false, default);
            }
            Task<CajaCancelacionResult> execution;
            Task<CajaCancelacionResult> cancellation;
            if (executionFirst)
            {
                execution = Execute();
                await WaitBlocked(database.ConnectionString, executeName);
                cancellation = Cancel();
                await WaitBlocked(database.ConnectionString, cancelName);
            }
            else
            {
                cancellation = Cancel();
                await WaitBlocked(database.ConnectionString, cancelName);
                execution = Execute();
                await WaitBlocked(database.ConnectionString, executeName);
            }
            await transaction.CommitAsync();
            var expected = executionFirst ? CajaStates.OperacionCompletada : CajaStates.OperacionCancelada;
            Assert.Equal(expected, (await execution.WaitAsync(TimeSpan.FromSeconds(20))).Estado);
            Assert.Equal(expected, (await cancellation.WaitAsync(TimeSpan.FromSeconds(20))).Estado);
            await using var verify = database.CreateContext();
            Assert.Equal(expected, await verify.OperacionesCaja.Where(x => x.IdOperacion == id).Select(x => x.Estado).SingleAsync());
            Assert.Equal(executionFirst ? 1 : 0, await verify.TurnosCaja.CountAsync());
            Assert.Equal(executionFirst ? 0 : 1, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "OperacionCajaCancelada"));
        }
        finally { await database.DisposeAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Real_payment_and_close_serialize_on_shift_in_both_orders(bool paymentFirst)
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            var paymentId = Guid.NewGuid();
            var closeId = Guid.NewGuid();
            int shiftId;
            int orderId;
            const string reference = "prueba-carrera";
            await using (var setup = database.CreateContext())
            {
                (await setup.Mesas.SingleAsync(x => x.IdMesa == 1)).Estado = MesaStates.Ocupada;
                var product = new Producto { Nombre = "Prueba", Categoria = "Comida", Precio = 10m, Disponible = true };
                var order = new Pedido { IdMesa = 1, IdUsuario = 1, FechaHora = DateTimeOffset.UtcNow, Estado = PedidoStates.Listo, Subtotal = 10m, Total = 10m };
                setup.Productos.Add(product);
                setup.Pedidos.Add(order);
                await setup.SaveChangesAsync();
                orderId = order.IdPedido;
                var command = new Comanda { IdPedido = orderId, IdUsuario = 1, FechaHora = DateTimeOffset.UtcNow, Estado = "Lista" };
                command.Detalles.Add(new DetallePedido { IdProducto = product.IdProducto, NombreProducto = product.Nombre, Cantidad = 1, PrecioUnitario = 10m, Subtotal = 10m, Notas = "" });
                setup.Comandas.Add(command);
                await setup.SaveChangesAsync();
                var audit = Audit(setup);
                var service = new CajaOperacionesService(setup, audit, TimeProvider.System, new CajaAperturaExecutor(setup, audit, TimeProvider.System));
                var openingId = Guid.NewGuid();
                Assert.Equal(200, (await service.PrepararAsync(openingId, 1, CajaOperacionCanonica.Abrir(0, true), default)).Status);
                Assert.Equal(201, (await service.EjecutarAsync(openingId, 1, default)).Status);
                shiftId = await setup.TurnosCaja.Select(x => x.IdTurnoCaja).SingleAsync();
                var loaded = await setup.Pedidos.AsNoTracking().Include(x => x.Comandas).ThenInclude(x => x.Detalles).SingleAsync(x => x.IdPedido == orderId);
                var intent = CajaOperacionCanonica.Cobrar(orderId, shiftId, 10m, CajaElegibilidad.Evaluar(loaded).HuellaLineas,
                    CajaStates.Transferencia, null, reference, true, false);
                Assert.Equal(200, (await service.PrepararAsync(paymentId, 1, intent, default)).Status);
                Assert.Equal(200, (await service.PrepararAsync(closeId, 1, CajaOperacionCanonica.Cerrar(shiftId, 0, null, true), default)).Status);
            }

            await using var blocker = new NpgsqlConnection(database.ConnectionString);
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            if (paymentFirst)
            {
                var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"elcarbonazo:caja:referencia:v1:{CajaStates.Transferencia}:{reference}"));
                await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", blocker, transaction);
                command.Parameters.AddWithValue("key", BinaryPrimitives.ReadInt64BigEndian(digest));
                await command.ExecuteNonQueryAsync();
            }
            else
            {
                await using var command = new NpgsqlCommand("SELECT id_turno_caja FROM turno_caja WHERE id_turno_caja = @id FOR UPDATE", blocker, transaction);
                command.Parameters.AddWithValue("id", shiftId);
                await command.ExecuteScalarAsync();
            }
            var paymentName = $"caja_execute_{Guid.NewGuid():N}";
            var closeName = $"caja_close_{Guid.NewGuid():N}";
            async Task<CajaCancelacionResult> Pay()
            {
                await using var context = NamedContext(database.ConnectionString, paymentName);
                var audit = Audit(context);
                return await new CajaOperacionesService(context, audit, TimeProvider.System, new CajaCobroExecutor(context, audit, TimeProvider.System))
                    .EjecutarAsync(paymentId, 1, default);
            }
            async Task<CajaCancelacionResult> Close()
            {
                await using var context = NamedContext(database.ConnectionString, closeName);
                var audit = Audit(context);
                return await new CajaOperacionesService(context, audit, TimeProvider.System, new CajaCierreExecutor(context, audit, TimeProvider.System))
                    .EjecutarAsync(closeId, 1, default);
            }
            Task<CajaCancelacionResult> payment;
            Task<CajaCancelacionResult> close;
            if (paymentFirst)
            {
                payment = Pay();
                await WaitBlocked(database.ConnectionString, paymentName);
                close = Close();
                await WaitBlocked(database.ConnectionString, closeName);
            }
            else
            {
                close = Close();
                await WaitBlocked(database.ConnectionString, closeName);
                payment = Pay();
                await WaitBlocked(database.ConnectionString, paymentName);
            }
            await transaction.CommitAsync();
            var paid = await payment.WaitAsync(TimeSpan.FromSeconds(20));
            var closed = await close.WaitAsync(TimeSpan.FromSeconds(20));
            await using var verify = database.CreateContext();
            Assert.Equal(CajaStates.OperacionCompletada, paid.Estado);
            Assert.Equal(PedidoStates.Pagado, await verify.Pedidos.Where(x => x.IdPedido == orderId).Select(x => x.Estado).SingleAsync());
            Assert.Equal(1, await verify.Cobros.CountAsync(x => x.IdPedido == orderId));
            Assert.Equal(1, await verify.ComprobantesCobroLineas.CountAsync());
            Assert.Equal(CajaStates.OperacionCompletada, await verify.OperacionesCaja.Where(x => x.IdOperacion == paymentId).Select(x => x.Estado).SingleAsync());
            Assert.Equal(1, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "CobroRegistrado"));
            var shift = await verify.TurnosCaja.AsNoTracking().SingleAsync(x => x.IdTurnoCaja == shiftId);
            if (paymentFirst)
            {
                Assert.Equal(CajaStates.OperacionCompletada, closed.Estado);
                Assert.Equal(CajaStates.TurnoCerrado, shift.Estado);
                Assert.Equal(10m, shift.TotalTransferencia);
            }
            else
            {
                Assert.Equal("OPERACION_NO_TERMINAL", closed.Code);
                Assert.Equal(CajaStates.TurnoAbierto, shift.Estado);
            }
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Real_payment_cancellation_never_reverts_a_commit_or_executes_a_cancelled_intent()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            var paymentIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
            var orderIds = new List<int>();
            await using (var setup = database.CreateContext())
            {
                var audit = Audit(setup);
                var opening = new CajaOperacionesService(setup, audit, TimeProvider.System, new CajaAperturaExecutor(setup, audit, TimeProvider.System));
                var openingId = Guid.NewGuid();
                Assert.Equal(200, (await opening.PrepararAsync(openingId, 1, CajaOperacionCanonica.Abrir(0, true), default)).Status);
                Assert.Equal(201, (await opening.EjecutarAsync(openingId, 1, default)).Status);
                var shiftId = await setup.TurnosCaja.Select(x => x.IdTurnoCaja).SingleAsync();
                var product = new Producto { Nombre = "Prueba", Categoria = "Comida", Precio = 10m, Disponible = true };
                setup.Productos.Add(product);
                await setup.SaveChangesAsync();
                foreach (var tableId in new[] { 1, 2 })
                {
                    (await setup.Mesas.SingleAsync(x => x.IdMesa == tableId)).Estado = MesaStates.Ocupada;
                    var order = new Pedido { IdMesa = tableId, IdUsuario = 1, FechaHora = DateTimeOffset.UtcNow, Estado = PedidoStates.Listo, Subtotal = 10m, Total = 10m };
                    setup.Pedidos.Add(order);
                    await setup.SaveChangesAsync();
                    orderIds.Add(order.IdPedido);
                    var command = new Comanda { IdPedido = order.IdPedido, IdUsuario = 1, FechaHora = DateTimeOffset.UtcNow, Estado = "Lista" };
                    command.Detalles.Add(new DetallePedido { IdProducto = product.IdProducto, NombreProducto = product.Nombre, Cantidad = 1, PrecioUnitario = 10m, Subtotal = 10m, Notas = "" });
                    setup.Comandas.Add(command);
                    await setup.SaveChangesAsync();
                    var loaded = await setup.Pedidos.AsNoTracking().Include(x => x.Comandas).ThenInclude(x => x.Detalles).SingleAsync(x => x.IdPedido == order.IdPedido);
                    var intent = CajaOperacionCanonica.Cobrar(order.IdPedido, shiftId, 10m, CajaElegibilidad.Evaluar(loaded).HuellaLineas, CajaStates.Efectivo, 15m, null, false, false);
                    Assert.Equal(200, (await opening.PrepararAsync(paymentIds[orderIds.Count - 1], 1, intent, default)).Status);
                }
            }
            await using (var execution = database.CreateContext())
            {
                var audit = Audit(execution);
                var service = new CajaOperacionesService(execution, audit, TimeProvider.System, new CajaCobroExecutor(execution, audit, TimeProvider.System));
                Assert.Equal(CajaStates.OperacionCompletada, (await service.EjecutarAsync(paymentIds[0], 1, default)).Estado);
            }
            await using (var cancellation = database.CreateContext())
            {
                var service = new CajaCancelacionService(cancellation, Audit(cancellation), TimeProvider.System);
                Assert.Equal(CajaStates.OperacionCompletada, (await service.CancelarAsync(paymentIds[0], 1, false, default)).Estado);
                Assert.Equal(CajaStates.OperacionCancelada, (await service.CancelarAsync(paymentIds[1], 1, false, default)).Estado);
            }
            await using (var delayed = database.CreateContext())
            {
                var audit = Audit(delayed);
                var service = new CajaOperacionesService(delayed, audit, TimeProvider.System, new CajaCobroExecutor(delayed, audit, TimeProvider.System));
                Assert.Equal(CajaStates.OperacionCancelada, (await service.EjecutarAsync(paymentIds[1], 1, default)).Estado);
            }
            await using var verify = database.CreateContext();
            var paid = await verify.Pedidos.AsNoTracking().Where(x => x.IdPedido == orderIds[0]).Select(x => x.Estado).SingleAsync();
            var cancelled = await verify.Pedidos.AsNoTracking().Where(x => x.IdPedido == orderIds[1]).Select(x => x.Estado).SingleAsync();
            Assert.Equal(PedidoStates.Pagado, paid);
            Assert.Equal(PedidoStates.Listo, cancelled);
            var payment = Assert.Single(await verify.Cobros.AsNoTracking().ToListAsync());
            Assert.Equal(orderIds[0], payment.IdPedido);
            Assert.Equal(5m, payment.Vuelto);
            Assert.Equal(1, await verify.ComprobantesCobroLineas.CountAsync());
            Assert.Equal(1, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "CobroRegistrado"));
            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var duplicate = new NpgsqlCommand("""
                INSERT INTO cobro (id_pedido, id_turno_caja, id_usuario, id_operacion, metodo_pago,
                    subtotal, impuestos, total, efectivo_recibido, vuelto, referencia_externa,
                    verificacion_externa_confirmada, referencia_repetida_confirmada, fecha_hora)
                SELECT id_pedido, id_turno_caja, id_usuario, @other_operation, metodo_pago,
                    subtotal, impuestos, total, efectivo_recibido, vuelto, referencia_externa,
                    verificacion_externa_confirmada, referencia_repetida_confirmada, fecha_hora
                FROM cobro WHERE id_pedido = @order_id
                """, connection);
            duplicate.Parameters.AddWithValue("other_operation", paymentIds[1]);
            duplicate.Parameters.AddWithValue("order_id", orderIds[0]);
            var violation = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, violation.SqlState);
            Assert.Equal("IX_cobro_id_pedido", violation.ConstraintName);
        }
        finally { await database.DisposeAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Payment_prepare_and_close_serialize_through_real_global_barrier(bool prepareFirst)
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            var closeId = Guid.NewGuid();
            var paymentId = Guid.NewGuid();
            int shiftId;
            int orderId;
            string? fingerprint;
            await using (var setup = database.CreateContext())
            {
                var table = await setup.Mesas.SingleAsync(x => x.IdMesa == 1);
                table.Estado = MesaStates.Ocupada;
                var product = new Producto { Nombre = "Prueba", Categoria = "Comida", Precio = 10m, Disponible = true };
                var order = new Pedido { IdMesa = 1, IdUsuario = 1, FechaHora = DateTimeOffset.UtcNow, Estado = PedidoStates.Listo, Subtotal = 10m, Total = 10m };
                setup.Productos.Add(product);
                setup.Pedidos.Add(order);
                await setup.SaveChangesAsync();
                orderId = order.IdPedido;
                var command = new Comanda { IdPedido = orderId, IdUsuario = 1, FechaHora = DateTimeOffset.UtcNow, Estado = "Lista" };
                command.Detalles.Add(new DetallePedido { IdProducto = product.IdProducto, NombreProducto = product.Nombre, Cantidad = 1, PrecioUnitario = 10m, Subtotal = 10m, Notas = "" });
                setup.Comandas.Add(command);
                await setup.SaveChangesAsync();
                var audit = Audit(setup);
                var opening = new CajaOperacionesService(setup, audit, TimeProvider.System, new CajaAperturaExecutor(setup, audit, TimeProvider.System));
                var openingId = Guid.NewGuid();
                Assert.Equal(200, (await opening.PrepararAsync(openingId, 1, CajaOperacionCanonica.Abrir(0, true), default)).Status);
                Assert.Equal(201, (await opening.EjecutarAsync(openingId, 1, default)).Status);
                shiftId = await setup.TurnosCaja.Select(x => x.IdTurnoCaja).SingleAsync();
                var loaded = await setup.Pedidos.AsNoTracking().Include(x => x.Comandas).ThenInclude(x => x.Detalles).SingleAsync(x => x.IdPedido == orderId);
                fingerprint = CajaElegibilidad.Evaluar(loaded).HuellaLineas;
                Assert.NotNull(fingerprint);
                Assert.Equal(200, (await opening.PrepararAsync(closeId, 1, CajaOperacionCanonica.Cerrar(shiftId, 0, null, true), default)).Status);
            }

            await using var blocker = new NpgsqlConnection(database.ConnectionString);
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            var sql = prepareFirst ? "SELECT id_mesa FROM mesa WHERE id_mesa = 1 FOR UPDATE" : "SELECT id_turno_caja FROM turno_caja WHERE id_turno_caja = @id FOR UPDATE";
            await using (var command = new NpgsqlCommand(sql, blocker, transaction))
            {
                if (!prepareFirst) command.Parameters.AddWithValue("id", shiftId);
                await command.ExecuteScalarAsync();
            }
            var prepareName = $"caja_prepare_{Guid.NewGuid():N}";
            var closeName = $"caja_close_{Guid.NewGuid():N}";
            async Task<CajaCancelacionResult> Prepare()
            {
                await using var context = NamedContext(database.ConnectionString, prepareName);
                var service = new CajaOperacionesService(context, Audit(context), TimeProvider.System);
                var intent = CajaOperacionCanonica.Cobrar(orderId, shiftId, 10m, fingerprint, CajaStates.Efectivo, 10m, null, false, false);
                return await service.PrepararAsync(paymentId, 1, intent, default);
            }
            async Task<CajaCancelacionResult> Close()
            {
                await using var context = NamedContext(database.ConnectionString, closeName);
                var audit = Audit(context);
                var service = new CajaOperacionesService(context, audit, TimeProvider.System, new CajaCierreExecutor(context, audit, TimeProvider.System));
                return await service.EjecutarAsync(closeId, 1, default);
            }
            Task<CajaCancelacionResult> prepare;
            Task<CajaCancelacionResult> close;
            if (prepareFirst)
            {
                prepare = Prepare();
                await WaitBlocked(database.ConnectionString, prepareName);
                close = Close();
                await WaitBlocked(database.ConnectionString, closeName);
            }
            else
            {
                close = Close();
                await WaitBlocked(database.ConnectionString, closeName);
                prepare = Prepare();
                await WaitBlocked(database.ConnectionString, prepareName);
            }
            await transaction.CommitAsync();
            var prepared = await prepare.WaitAsync(TimeSpan.FromSeconds(20));
            var closed = await close.WaitAsync(TimeSpan.FromSeconds(20));
            await using var verify = database.CreateContext();
            if (prepareFirst)
            {
                Assert.Equal(CajaStates.OperacionPreparada, prepared.Estado);
                Assert.Equal("OPERACION_NO_TERMINAL", closed.Code);
                Assert.Equal(CajaStates.TurnoAbierto, await verify.TurnosCaja.Where(x => x.IdTurnoCaja == shiftId).Select(x => x.Estado).SingleAsync());
            }
            else
            {
                Assert.Equal("TURNO_NO_ABIERTO", prepared.Code);
                Assert.Equal(CajaStates.OperacionCompletada, closed.Estado);
                Assert.Equal(CajaStates.TurnoCerrado, await verify.TurnosCaja.Where(x => x.IdTurnoCaja == shiftId).Select(x => x.Estado).SingleAsync());
                Assert.False(await verify.OperacionesCaja.AnyAsync(x => x.IdOperacion == paymentId));
            }
            Assert.Empty(await verify.Cobros.ToListAsync());
        }
        finally { await database.DisposeAsync(); }
    }

    private static AppDbContext NamedContext(string connection, string name) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { ApplicationName = name }.ConnectionString).Options);
    private static AuditRecorder Audit(AppDbContext context) => new(context, new HttpContextAccessor(), TimeProvider.System);

    private static async Task WaitBlocked(string connection, string name)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync(timeout.Token);
        while (true)
        {
            await using var check = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name = @name AND cardinality(pg_blocking_pids(pid)) > 0)", observer);
            check.Parameters.AddWithValue("name", name);
            if ((bool)(await check.ExecuteScalarAsync(timeout.Token))!) return;
            await Task.Delay(25, timeout.Token);
        }
    }
}
