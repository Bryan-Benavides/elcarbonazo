using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Services;
using Xunit;

namespace ElCarbonazo.Api.Tests;

internal static class InventarioTestSetup
{
    internal static async Task PrepararProductoAsync(AppDbContext context, int productId)
    {
        var audit = new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System);
        var inventory = new InventarioService(context, audit, TimeProvider.System);
        var ingredient = await inventory.CrearIngredienteAsync(new($"Insumo {productId}", "unidad", 0, null, null), 1, default);
        Assert.Equal(201, ingredient.Status);
        var recipe = new RecetasService(context, audit, TimeProvider.System);
        Assert.Equal(200, (await recipe.GuardarAsync(productId, new(true, null, null,
            [new(ingredient.Id!.Value, 1m)]), 1, default)).Status);
        Assert.Equal(201, (await inventory.RegistrarMovimientoAsync(
            new(ingredient.Id.Value, "EntradaCompra", 99999m, null), 1, default)).Status);
        context.ChangeTracker.Clear();
    }
}
