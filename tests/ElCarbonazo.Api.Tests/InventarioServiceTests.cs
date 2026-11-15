using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class InventarioServiceTests
{
    [Fact]
    public async Task Ingredient_and_manual_movements_are_exact_audited_and_versioned()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var context = db.CreateContext();
            var audit = new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System);
            var service = new InventarioService(context, audit, TimeProvider.System);
            var created = await service.CrearIngredienteAsync(new("  CARNE\u0301  ", "lb", 0.5m, null, null), 1, default);
            Assert.Equal(201, created.Status);
            context.ChangeTracker.Clear();
            var duplicate = await service.CrearIngredienteAsync(new("carné", "lb", 0, null, null), 1, default);
            Assert.Equal("INGREDIENTE_DUPLICADO", duplicate.Code);
            context.ChangeTracker.Clear();
            var id = created.Id!.Value;
            Assert.Equal(201, (await service.RegistrarMovimientoAsync(new(id, "EntradaCompra", 1.2500m, null), 1, default)).Status);
            context.ChangeTracker.Clear();
            Assert.Equal(201, (await service.RegistrarMovimientoAsync(new(id, "AjusteNegativo", 0.3500m, "Merma"), 1, default)).Status);
            context.ChangeTracker.Clear();
            Assert.Equal("STOCK_INSUFICIENTE", (await service.RegistrarMovimientoAsync(new(id, "AjusteNegativo", 2m, "Merma"), 1, default)).Code);
            Assert.Equal(400, (await service.RegistrarMovimientoAsync(new(id, "AjustePositivo", 0.00001m, "Ajuste"), 1, default)).Status);
            Assert.Equal(400, (await service.RegistrarMovimientoAsync(new(id, "AjustePositivo", 1m, null), 1, default)).Status);
            context.ChangeTracker.Clear();
            var ingredient = await context.Ingredientes.SingleAsync();
            Assert.Equal(0.9000m, ingredient.StockActual);
            Assert.Equal(3, ingredient.Version);
            Assert.Equal(2, await context.MovimientosInventario.CountAsync());
            Assert.Equal(2, await context.EventosAuditoria.CountAsync(x => x.TipoEvento == "MovimientoInventarioRegistrado"));
            Assert.Equal("UNIDAD_EN_USO", (await service.ActualizarIngredienteAsync(id, new("Carné", "kg", 0.5m, true, "3"), 1, default)).Code);
            Assert.Equal("VERSION_CONFLICT", (await service.ActualizarIngredienteAsync(id, new("Carné", "lb", 0.5m, true, "1"), 1, default)).Code);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Audit_failure_rolls_back_manual_stock_and_ledger()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var context = db.CreateContext();
            var normal = new InventarioService(context, new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
            var created = await normal.CrearIngredienteAsync(new("Agua", "lt", 0, null, null), 1, default);
            context.ChangeTracker.Clear();
            var failing = new InventarioService(context, new FailingAudit(), TimeProvider.System);
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.RegistrarMovimientoAsync(
                new(created.Id!.Value, "EntradaCompra", 2m, null), 1, default));
            context.ChangeTracker.Clear();
            Assert.Equal(0, (await context.Ingredientes.SingleAsync()).StockActual);
            Assert.Empty(await context.MovimientosInventario.ToListAsync());
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Recipe_replacement_checks_version_and_rolls_back_when_audit_fails()
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
            context.ChangeTracker.Clear();
            var product = await context.Productos.AsNoTracking().SingleAsync();
            var recipes = new RecetasService(context, audit, TimeProvider.System);
            var input = new RecetaInput(true, null, null, [new(ingredient.Id!.Value, 0.3500m)]);
            Assert.Equal(200, (await recipes.GuardarAsync(product.IdProducto, input, 1, default)).Status);
            context.ChangeTracker.Clear();
            Assert.Equal("VERSION_CONFLICT", (await recipes.GuardarAsync(product.IdProducto, input, 1, default)).Code);
            Assert.Equal(400, (await recipes.GuardarAsync(product.IdProducto,
                new(true, null, "1", [new(ingredient.Id.Value, 1), new(ingredient.Id.Value, 2)]), 1, default)).Status);
            var failing = new RecetasService(context, new FailingAudit(), TimeProvider.System);
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.GuardarAsync(product.IdProducto,
                new(true, null, "1", [new(ingredient.Id.Value, 0.4m)]), 1, default));
            context.ChangeTracker.Clear();
            var recipe = await context.Recetas.Include(x => x.Detalles).SingleAsync();
            Assert.Equal(1, recipe.Version);
            Assert.Equal(0.3500m, recipe.Detalles.Single().CantidadRequerida);
        }
        finally { await db.DisposeAsync(); }
    }

    private sealed class FailingAudit : IAuditRecorder
    {
        public void Record(string eventType, string result, int? userId = null, Guid? sessionId = null,
            string? entityType = null, string? entityId = null, IReadOnlyDictionary<string, object?>? details = null) =>
            throw new InvalidOperationException("Simulated audit failure.");
    }
}
