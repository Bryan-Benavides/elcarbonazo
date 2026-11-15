using System.Text.Json.Nodes;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class InventarioEnviosTests
{
    [Fact]
    public async Task Deactivation_waits_for_send_and_rejects_stale_ingredient_version()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var setup = db.CreateContext();
            await setup.Database.ExecuteSqlRawAsync("INSERT INTO producto (nombre, categoria, precio, disponible) VALUES ('Plato', 'Comida', 10, true)");
            var product = await setup.Productos.AsNoTracking().SingleAsync();
            await InventarioTestSetup.PrepararProductoAsync(setup, product.IdProducto);
            var ingredient = await setup.Ingredientes.AsNoTracking().SingleAsync();
            var table = await setup.Mesas.AsNoTracking().SingleAsync(x => x.IdMesa == 1);
            using var gate = new Gate();
            var name = $"inv_wait_{Guid.NewGuid():N}";
            await using var first = db.CreateContext();
            var waitingConnection = new NpgsqlConnectionStringBuilder(db.ConnectionString) { ApplicationName = name };
            await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(waitingConnection.ConnectionString).Options);
            var blockingAudit = new BlockingAudit(new AuditRecorder(first, new TestHttpContextAccessor(), TimeProvider.System), gate);
            var normalAudit = new AuditRecorder(second, new TestHttpContextAccessor(), TimeProvider.System);
            var send = Task.Run(() => new EnviosService(first, blockingAudit, TimeProvider.System).EnviarAsync(
                new(Guid.NewGuid(), 1, null, table.Version, [new(product.IdProducto, product.Version, 1, "")]), 1, default));
            Task<InventarioResult>? deactivate = null;
            try
            {
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                deactivate = Task.Run(() => new InventarioService(second, normalAudit, TimeProvider.System)
                    .ActualizarIngredienteAsync(ingredient.IdIngrediente,
                        new(ingredient.Nombre, ingredient.UnidadMedida, ingredient.StockMinimo, false, ingredient.Version.ToString()), 1, default));
                await WaitForBlockAsync(db.ConnectionString, name);
            }
            finally { gate.Release.Set(); }
            Assert.Equal(201, (await send.WaitAsync(TimeSpan.FromSeconds(20))).Status);
            Assert.Equal("VERSION_CONFLICT", (await deactivate!.WaitAsync(TimeSpan.FromSeconds(20))).Code);
            setup.ChangeTracker.Clear();
            Assert.True((await setup.Ingredientes.SingleAsync()).Activo);
            Assert.Equal(1, await setup.MovimientosInventario.CountAsync(x => x.TipoMovimiento == "ConsumoPedido"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recipe_creation_and_ingredient_deactivation_serialize_in_both_orders(bool recipeFirst)
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var setup = db.CreateContext();
            await setup.Database.ExecuteSqlRawAsync("INSERT INTO producto (nombre, categoria, precio, disponible) VALUES ('Plato', 'Comida', 10, true)");
            var product = await setup.Productos.AsNoTracking().SingleAsync();
            var audit = new AuditRecorder(setup, new TestHttpContextAccessor(), TimeProvider.System);
            var created = await new InventarioService(setup, audit, TimeProvider.System)
                .CrearIngredienteAsync(new("Carne", "lb", 0, null, null), 1, default);
            var ingredientId = created.Id!.Value;
            using var gate = new Gate();
            var name = $"inv_wait_{Guid.NewGuid():N}";
            await using var first = db.CreateContext();
            var waitingConnection = new NpgsqlConnectionStringBuilder(db.ConnectionString) { ApplicationName = name };
            await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(waitingConnection.ConnectionString).Options);
            var blockingAudit = new BlockingAudit(new AuditRecorder(first, new TestHttpContextAccessor(), TimeProvider.System), gate);
            var normalAudit = new AuditRecorder(second, new TestHttpContextAccessor(), TimeProvider.System);
            Task<InventarioResult>? recipeTask = null;
            Task<InventarioResult>? deactivateTask = null;
            try
            {
                if (recipeFirst)
                    recipeTask = Task.Run(() => new RecetasService(first, blockingAudit, TimeProvider.System)
                        .GuardarAsync(product.IdProducto, new(true, null, null, [new(ingredientId, 1m)]), 1, default));
                else
                    deactivateTask = Task.Run(() => new InventarioService(first, blockingAudit, TimeProvider.System)
                        .ActualizarIngredienteAsync(ingredientId, new("Carne", "lb", 0, false, "1"), 1, default));
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                if (recipeFirst)
                    deactivateTask = Task.Run(() => new InventarioService(second, normalAudit, TimeProvider.System)
                        .ActualizarIngredienteAsync(ingredientId, new("Carne", "lb", 0, false, "1"), 1, default));
                else
                    recipeTask = Task.Run(() => new RecetasService(second, normalAudit, TimeProvider.System)
                        .GuardarAsync(product.IdProducto, new(true, null, null, [new(ingredientId, 1m)]), 1, default));
                await WaitForBlockAsync(db.ConnectionString, name);
            }
            finally { gate.Release.Set(); }
            var recipeResult = await recipeTask!.WaitAsync(TimeSpan.FromSeconds(20));
            var deactivateResult = await deactivateTask!.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(recipeFirst ? 200 : 409, recipeResult.Status);
            Assert.Equal(recipeFirst ? 409 : 200, deactivateResult.Status);
            Assert.Equal(recipeFirst ? "INGREDIENTE_EN_USO" : null, deactivateResult.Code);
            Assert.Equal(recipeFirst ? null : "INGREDIENTE_INACTIVO", recipeResult.Code);
            setup.ChangeTracker.Clear();
            Assert.Equal(recipeFirst, (await setup.Ingredientes.SingleAsync()).Activo);
            Assert.Equal(recipeFirst ? 1 : 0, await setup.Recetas.CountAsync());
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recipe_replacement_and_send_serialize_in_both_orders(bool recipeFirst)
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var setup = db.CreateContext();
            await setup.Database.ExecuteSqlRawAsync("INSERT INTO producto (nombre, categoria, precio, disponible) VALUES ('Plato', 'Comida', 10, true)");
            var product = await setup.Productos.AsNoTracking().SingleAsync();
            await InventarioTestSetup.PrepararProductoAsync(setup, product.IdProducto);
            var ingredient = await setup.Ingredientes.AsNoTracking().SingleAsync();
            var table = await setup.Mesas.AsNoTracking().SingleAsync(x => x.IdMesa == 1);
            using var gate = new Gate();
            var name = $"inv_wait_{Guid.NewGuid():N}";
            await using var first = db.CreateContext();
            var waitingConnection = new NpgsqlConnectionStringBuilder(db.ConnectionString) { ApplicationName = name };
            await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(waitingConnection.ConnectionString).Options);
            var blockingAudit = new BlockingAudit(new AuditRecorder(first, new TestHttpContextAccessor(), TimeProvider.System), gate);
            var normalAudit = new AuditRecorder(second, new TestHttpContextAccessor(), TimeProvider.System);
            var request = new EnvioRequest(Guid.NewGuid(), 1, null, table.Version, [new(product.IdProducto, product.Version, 1, "")]);
            var recipe = new RecetaInput(true, null, "1", [new(ingredient.IdIngrediente, 2m)]);
            Task<InventarioResult>? recipeTask = null;
            Task<EnvioResult>? sendTask = null;
            try
            {
                if (recipeFirst)
                    recipeTask = Task.Run(() => new RecetasService(first, blockingAudit, TimeProvider.System).GuardarAsync(product.IdProducto, recipe, 1, default));
                else
                    sendTask = Task.Run(() => new EnviosService(first, blockingAudit, TimeProvider.System).EnviarAsync(request, 1, default));
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                if (recipeFirst)
                    sendTask = Task.Run(() => new EnviosService(second, normalAudit, TimeProvider.System).EnviarAsync(request, 1, default));
                else
                    recipeTask = Task.Run(() => new RecetasService(second, normalAudit, TimeProvider.System).GuardarAsync(product.IdProducto, recipe, 1, default));
                await WaitForBlockAsync(db.ConnectionString, name);
            }
            finally { gate.Release.Set(); }
            Assert.Equal(200, (await recipeTask!.WaitAsync(TimeSpan.FromSeconds(20))).Status);
            Assert.Equal(201, (await sendTask!.WaitAsync(TimeSpan.FromSeconds(20))).Status);
            setup.ChangeTracker.Clear();
            var consumed = await setup.MovimientosInventario.SingleAsync(x => x.TipoMovimiento == "ConsumoPedido");
            Assert.Equal(recipeFirst ? -2m : -1m, consumed.CantidadDelta);
            Assert.Equal(recipeFirst ? 99997m : 99998m, (await setup.Ingredientes.SingleAsync()).StockActual);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Audit_failure_rolls_back_order_stock_and_consumption()
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
            var table = await context.Mesas.AsNoTracking().SingleAsync(x => x.IdMesa == 1);
            var service = new EnviosService(context, new FailingAudit(), TimeProvider.System);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnviarAsync(
                new(Guid.NewGuid(), 1, null, table.Version, [new(product.IdProducto, product.Version, 1, "")]), 1, default));
            context.ChangeTracker.Clear();
            Assert.Empty(await context.Pedidos.ToListAsync());
            Assert.Empty(await context.Comandas.ToListAsync());
            Assert.Empty(await context.EnviosPedido.ToListAsync());
            Assert.Equal(99999m, (await context.Ingredientes.SingleAsync()).StockActual);
            Assert.Equal(0, await context.MovimientosInventario.CountAsync(x => x.TipoMovimiento == "ConsumoPedido"));
            Assert.Equal("Libre", (await context.Mesas.AsNoTracking().SingleAsync(x => x.IdMesa == 1)).Estado);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Competing_orders_only_consume_last_stock_once()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var setup = db.CreateContext();
            await setup.Database.ExecuteSqlRawAsync("""
                INSERT INTO producto (nombre, categoria, precio, disponible) VALUES ('Plato', 'Comida', 10, true)
                """);
            var audit = new AuditRecorder(setup, new TestHttpContextAccessor(), TimeProvider.System);
            var inventory = new InventarioService(setup, audit, TimeProvider.System);
            var ingredient = await inventory.CrearIngredienteAsync(new("Carne", "lb", 0, null, null), 1, default);
            var product = await setup.Productos.AsNoTracking().SingleAsync();
            await new RecetasService(setup, audit, TimeProvider.System).GuardarAsync(product.IdProducto,
                new(true, null, null, [new(ingredient.Id!.Value, 1m)]), 1, default);
            await inventory.RegistrarMovimientoAsync(new(ingredient.Id.Value, "EntradaCompra", 1m, null), 1, default);
            var tables = await setup.Mesas.AsNoTracking().Where(x => x.IdMesa == 1 || x.IdMesa == 2).OrderBy(x => x.IdMesa).ToListAsync();
            await using var first = db.CreateContext();
            await using var second = db.CreateContext();
            var s1 = new EnviosService(first, new AuditRecorder(first, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
            var s2 = new EnviosService(second, new AuditRecorder(second, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
            var results = await Task.WhenAll(
                s1.EnviarAsync(new(Guid.NewGuid(), 1, null, tables[0].Version, [new(product.IdProducto, product.Version, 1, "")]), 1, default),
                s2.EnviarAsync(new(Guid.NewGuid(), 2, null, tables[1].Version, [new(product.IdProducto, product.Version, 1, "")]), 1, default));
            Assert.Single(results, x => x.Status == 201);
            Assert.Single(results, x => x.Code == "STOCK_INSUFICIENTE");
            setup.ChangeTracker.Clear();
            Assert.Equal(0, (await setup.Ingredientes.SingleAsync()).StockActual);
            Assert.Equal(1, await setup.Comandas.CountAsync());
            Assert.Equal(1, await setup.MovimientosInventario.CountAsync(x => x.TipoMovimiento == "ConsumoPedido"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Send_requires_recipe_aggregates_stock_and_replay_does_not_consume_twice()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var context = db.CreateContext();
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO producto (nombre, categoria, precio, disponible) VALUES ('Plato', 'Comida', 10, true)
                """);
            var audit = new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System);
            var inventory = new InventarioService(context, audit, TimeProvider.System);
            var ingredient = await inventory.CrearIngredienteAsync(new("Carne", "lb", 0, null, null), 1, default);
            var product = await context.Productos.AsNoTracking().SingleAsync();
            var mesa = await context.Mesas.AsNoTracking().SingleAsync(x => x.IdMesa == 1);
            var request = new EnvioRequest(Guid.NewGuid(), 1, null, mesa.Version,
                [new(product.IdProducto, product.Version, 2, ""), new(product.IdProducto, product.Version, 1, "")]);
            var service = new EnviosService(context, audit, TimeProvider.System);
            Assert.Equal("RECETA_NO_DISPONIBLE", (await service.EnviarAsync(request, 1, default)).Code);
            context.ChangeTracker.Clear();
            var recipes = new RecetasService(context, audit, TimeProvider.System);
            Assert.Equal(200, (await recipes.GuardarAsync(product.IdProducto,
                new(true, null, null, [new(ingredient.Id!.Value, 0.3500m)]), 1, default)).Status);
            context.ChangeTracker.Clear();
            Assert.Equal(201, (await inventory.RegistrarMovimientoAsync(new(ingredient.Id.Value, "EntradaCompra", 1m, null), 1, default)).Status);
            context.ChangeTracker.Clear();
            var insufficient = await service.EnviarAsync(request, 1, default);
            Assert.Equal("STOCK_INSUFICIENTE", insufficient.Code);
            Assert.Empty(await context.Pedidos.ToListAsync());
            Assert.Empty(await context.Comandas.ToListAsync());
            Assert.Equal(1m, (await context.Ingredientes.SingleAsync()).StockActual);
            context.ChangeTracker.Clear();
            Assert.Equal(201, (await inventory.RegistrarMovimientoAsync(new(ingredient.Id.Value, "EntradaCompra", 1m, null), 1, default)).Status);
            context.ChangeTracker.Clear();
            var first = await service.EnviarAsync(request, 1, default);
            Assert.Equal(201, first.Status);
            context.ChangeTracker.Clear();
            var repeat = await service.EnviarAsync(request, 1, default);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(first.Json!), JsonNode.Parse(repeat.Json!)));
            context.ChangeTracker.Clear();
            Assert.Equal(0.9500m, (await context.Ingredientes.SingleAsync()).StockActual);
            var consumption = await context.MovimientosInventario.SingleAsync(x => x.TipoMovimiento == "ConsumoPedido");
            Assert.Equal(-1.0500m, consumption.CantidadDelta);
            Assert.Equal(1, await context.Comandas.CountAsync());
            Assert.Equal(1, await context.EventosAuditoria.CountAsync(x => x.TipoEvento == "ConsumoInventarioPorPedido"));
        }
        finally { await db.DisposeAsync(); }
    }

    private sealed class FailingAudit : IAuditRecorder
    {
        public void Record(string eventType, string result, int? userId = null, Guid? sessionId = null,
            string? entityType = null, string? entityId = null, IReadOnlyDictionary<string, object?>? details = null) =>
            throw new InvalidOperationException("Simulated audit failure.");
    }

    private static async Task WaitForBlockAsync(string connectionString, string name)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                    WHERE application_name = @name AND cardinality(pg_blocking_pids(pid)) > 0)
                """, connection);
            command.Parameters.AddWithValue("name", name);
            if ((bool)(await command.ExecuteScalarAsync(timeout.Token))!) return;
            await Task.Delay(25, timeout.Token);
        }
    }

    private sealed class Gate : IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public void Dispose() => Release.Dispose();
    }

    private sealed class BlockingAudit(IAuditRecorder inner, Gate gate) : IAuditRecorder
    {
        public void Record(string eventType, string result, int? userId = null, Guid? sessionId = null,
            string? entityType = null, string? entityId = null, IReadOnlyDictionary<string, object?>? details = null)
        {
            inner.Record(eventType, result, userId, sessionId, entityType, entityId, details);
            gate.Entered.TrySetResult();
            if (!gate.Release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Inventory test barrier timed out.");
        }
    }
}
