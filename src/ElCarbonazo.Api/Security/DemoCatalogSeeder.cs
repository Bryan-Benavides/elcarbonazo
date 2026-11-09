using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ElCarbonazo.Api.Security;

public sealed class DemoCatalogSeeder(
    AppDbContext context,
    IHostEnvironment environment,
    IConfiguration configuration)
{
    private static readonly DemoProduct[] Products =
    [
        new("Gallo pinto DEMO", ProductoCategories.Comida, 85.00m),
        new("Quesillo DEMO", ProductoCategories.Comida, 95.00m),
        new("Tajadas con queso DEMO", ProductoCategories.Comida, 110.00m),
        new("Nacatamal DEMO", ProductoCategories.Comida, 125.00m),
        new("Café negro DEMO", ProductoCategories.Bebida, 35.00m),
        new("Jugo de naranja DEMO", ProductoCategories.Bebida, 55.00m),
        new("Limonada DEMO", ProductoCategories.Bebida, 50.00m),
        new("Agua embotellada DEMO", ProductoCategories.Bebida, 30.00m),
        new("Cerveza local DEMO", ProductoCategories.Licor, 80.00m),
        new("Ron servido DEMO", ProductoCategories.Licor, 100.00m)
    ];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() ||
            !configuration.GetValue<bool>("DemoCatalog:Enabled"))
        {
            throw new InvalidOperationException(
                "El catálogo DEMOSTRACIÓN requiere entorno Development y DemoCatalog:Enabled=true.");
        }

        var databaseName = await context.Database
            .SqlQueryRaw<string>("SELECT current_database() AS \"Value\"")
            .SingleAsync(cancellationToken);
        if (!databaseName.StartsWith("elcarbonazo_demo_", StringComparison.Ordinal) &&
            !databaseName.StartsWith("elcarbonazo_test_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "El catálogo DEMOSTRACIÓN solo puede cargarse en una base elcarbonazo_demo_* o elcarbonazo_test_*.");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "LOCK TABLE producto IN EXCLUSIVE MODE",
            cancellationToken);

        var existing = await context.Productos.AsNoTracking()
            .OrderBy(product => product.IdProducto)
            .ToListAsync(cancellationToken);
        if (existing.Count == 0)
        {
            context.Productos.AddRange(Products.Select(product => new Producto
            {
                Nombre = product.Name,
                Categoria = product.Category,
                Precio = product.Price,
                Disponible = true
            }));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var expected = Products.ToDictionary(product => product.Name, StringComparer.Ordinal);
        if (existing.Count != expected.Count ||
            existing.Select(product => product.Nombre).Distinct(StringComparer.Ordinal).Count() != expected.Count ||
            existing.Any(product =>
                !expected.TryGetValue(product.Nombre, out var seed) ||
                product.Categoria != seed.Category ||
                product.Precio != seed.Price ||
                !product.Disponible))
        {
            throw new InvalidOperationException(
                "La base ya contiene productos que no coinciden exactamente con el catálogo DEMOSTRACIÓN; no se modificó ningún producto.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private sealed record DemoProduct(string Name, string Category, decimal Price);
}

public static class DemoCatalogCommand
{
    public static async Task<int> RunAsync(
        DemoCatalogSeeder seeder,
        CancellationToken cancellationToken)
    {
        try
        {
            await seeder.SeedAsync(cancellationToken);
            Console.WriteLine("Catálogo DEMOSTRACIÓN cargado (o ya estaba cargado) sin sobrescribir productos.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
