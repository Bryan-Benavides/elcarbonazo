using System.Data;
using System.Globalization;
using System.Text;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ElCarbonazo.Api.Security;

/// <summary>Preparación explícita, exclusivamente local, sin alterar registros existentes.</summary>
public sealed class CatalogoDefensaSeeder(AppDbContext context, IAuditRecorder audit, TimeProvider clock)
{
    private const decimal MaxStock = 99999999.9999m;
    private static readonly string[] Units = ["lb", "kg", "oz", "g", "unidad", "ml", "lt"];

    public async Task CargarAsync(string directory, int actor, string expectedDatabase, CancellationToken ct)
    {
        if (actor <= 0 || (expectedDatabase != "elcarbonazo" &&
                           !expectedDatabase.StartsWith("elcarbonazo_test_", StringComparison.Ordinal)))
            throw new InvalidOperationException("Destino o usuario no autorizado.");
        var connection = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString());
        if (connection.Host is not ("localhost" or "127.0.0.1" or "::1") || connection.Database != expectedDatabase)
            throw new InvalidOperationException("La carga requiere una conexión local y el nombre de base confirmado.");
        var data = ReadData(directory);
        var actualDatabase = await context.Database.SqlQueryRaw<string>(
            "SELECT current_database() AS \"Value\"").SingleAsync(ct);
        if (actualDatabase != expectedDatabase)
            throw new InvalidOperationException("El destino efectivo no coincide con el destino confirmado.");

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await context.Database.ExecuteSqlRawAsync(
            "LOCK TABLE producto, ingrediente, receta, detalle_receta, movimiento_inventario IN EXCLUSIVE MODE", ct);
        if (!await context.Usuarios.AnyAsync(x => x.IdUsuario == actor && x.Activo && x.Rol == "Jefa", ct))
            throw new InvalidOperationException("Se requiere una Jefa activa para registrar la carga.");
        if (await context.Productos.AnyAsync(ct) || await context.Ingredientes.AnyAsync(ct) ||
            await context.Recetas.AnyAsync(ct) || await context.DetallesReceta.AnyAsync(ct) ||
            await context.MovimientosInventario.AnyAsync(ct))
        {
            if (await CoincideAsync(data, ct))
            {
                await transaction.CommitAsync(ct);
                return;
            }
            throw new InvalidOperationException("El catálogo o inventario no están vacíos; no se modificó nada.");
        }

        var now = clock.GetUtcNow();
        var products = data.Products.Select(p => new Producto
        {
            Nombre = p.Name,
            Categoria = p.Category,
            Precio = p.Price,
            Disponible = true
        }).ToArray();
        context.Productos.AddRange(products);
        var ingredients = data.Ingredients.Select(i => new Ingrediente
        {
            Nombre = i.Name,
            NombreClave = i.Name.ToLowerInvariant().Normalize(NormalizationForm.FormC),
            UnidadMedida = i.Unit,
            StockMinimo = i.Minimum,
            FechaCreacionUtc = now,
            FechaActualizacionUtc = now
        }).ToArray();
        context.Ingredientes.AddRange(ingredients);
        await context.SaveChangesAsync(ct);
        var byName = ingredients.ToDictionary(i => i.Nombre, StringComparer.Ordinal);
        foreach (var ingredient in ingredients)
            audit.Record("IngredienteCreado", "Exitoso", actor, entityType: "ingrediente",
                entityId: ingredient.IdIngrediente.ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < products.Length; i++)
        {
            var recipe = new Receta { IdProducto = products[i].IdProducto, Activa = true, FechaActualizacionUtc = now };
            foreach (var line in data.Recipes[i + 1])
                recipe.Detalles.Add(new DetalleReceta
                {
                    IdIngrediente = byName[line.Name].IdIngrediente,
                    CantidadRequerida = line.Quantity
                });
            context.Recetas.Add(recipe);
        }
        await context.SaveChangesAsync(ct);
        foreach (var recipe in context.ChangeTracker.Entries<Receta>().Select(e => e.Entity))
            audit.Record("RecetaGuardada", "Exitoso", actor, entityType: "receta",
                entityId: recipe.IdReceta.ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < ingredients.Length; i++)
        {
            var stock = data.Ingredients[i].Stock;
            if (stock == 0) continue;
            var ingredient = ingredients[i];
            ingredient.StockActual = stock;
            ingredient.Version++;
            context.MovimientosInventario.Add(new MovimientoInventario
            {
                IdIngrediente = ingredient.IdIngrediente,
                TipoMovimiento = "EntradaCompra",
                CantidadDelta = stock,
                StockAnterior = 0,
                StockPosterior = stock,
                IdUsuario = actor,
                FechaUtc = now
            });
        }
        await context.SaveChangesAsync(ct);
        foreach (var movement in context.ChangeTracker.Entries<MovimientoInventario>().Select(e => e.Entity))
            audit.Record("MovimientoInventarioRegistrado", "Exitoso", actor, entityType: "movimiento_inventario",
                entityId: movement.IdMovimiento.ToString(CultureInfo.InvariantCulture));
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<bool> CoincideAsync(Data data, CancellationToken ct)
    {
        var products = await context.Productos.AsNoTracking().ToListAsync(ct);
        var ingredients = await context.Ingredientes.AsNoTracking().ToListAsync(ct);
        var recipes = await context.Recetas.AsNoTracking().Include(x => x.Detalles).ToListAsync(ct);
        var movements = await context.MovimientosInventario.AsNoTracking().ToListAsync(ct);
        if (products.Count != data.Products.Length || ingredients.Count != data.Ingredients.Length ||
            recipes.Count != data.Recipes.Count || movements.Count != data.Ingredients.Count(x => x.Stock > 0))
            return false;
        var ingredientsByName = ingredients.ToDictionary(x => x.Nombre, StringComparer.Ordinal);
        foreach (var item in data.Ingredients)
        {
            if (!ingredientsByName.TryGetValue(item.Name, out var ingredient) || !ingredient.Activo ||
                ingredient.UnidadMedida != item.Unit || ingredient.StockActual != item.Stock ||
                ingredient.StockMinimo != item.Minimum ||
                ingredient.NombreClave != item.Name.ToLowerInvariant().Normalize(NormalizationForm.FormC))
                return false;
            var matching = movements.Where(x => x.IdIngrediente == ingredient.IdIngrediente).ToArray();
            if (item.Stock == 0 ? matching.Length != 0 :
                matching.Length != 1 || matching[0].TipoMovimiento != "EntradaCompra" ||
                matching[0].CantidadDelta != item.Stock || matching[0].StockAnterior != 0 ||
                matching[0].StockPosterior != item.Stock)
                return false;
        }
        for (var i = 0; i < data.Products.Length; i++)
        {
            var product = data.Products[i];
            var matches = products.Where(x => x.Nombre == product.Name).ToArray();
            if (matches.Length != 1 || matches[0].Categoria != product.Category ||
                matches[0].Precio != product.Price || !matches[0].Disponible)
                return false;
            var matchingRecipes = recipes.Where(x => x.IdProducto == matches[0].IdProducto).ToArray();
            if (matchingRecipes.Length != 1 || !matchingRecipes[0].Activa ||
                matchingRecipes[0].NotasPreparacion is not null ||
                matchingRecipes[0].Detalles.Count != data.Recipes[i + 1].Length)
                return false;
            foreach (var line in data.Recipes[i + 1])
                if (!matchingRecipes[0].Detalles.Any(x => x.IdIngrediente == ingredientsByName[line.Name].IdIngrediente &&
                                                         x.CantidadRequerida == line.Quantity))
                    return false;
        }
        return true;
    }

    private static Data ReadData(string directory)
    {
        static string[] Lines(string path, string header)
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length < 2 || lines[0] != header) throw new InvalidDataException("Cabecera inválida.");
            return lines[1..];
        }
        static decimal Number(string text, decimal maximum, bool zero = false)
        {
            if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ||
                n < 0 || (!zero && n == 0) || n > maximum || decimal.Round(n, 4) != n)
                throw new InvalidDataException("Cantidad inválida.");
            return n;
        }
        static bool Name(string text) => text.Length is > 0 and <= 120 && text == text.Trim() &&
            text == text.Normalize(NormalizationForm.FormC) && !text.Any(char.IsControl);

        var productLines = Lines(Path.Combine(directory, "catalogo-productos.csv"), "numero,nombre,categoria,precio_nio");
        if (productLines.Length != 90) throw new InvalidDataException("Se requieren 90 productos.");
        var products = productLines.Select((line, i) =>
        {
            var parts = line.Split(',');
            if (parts.Length != 4 || parts[0] != (i + 1).ToString(CultureInfo.InvariantCulture) ||
                !Name(parts[1]) || parts[2] is not ("Comida" or "Bebida" or "Licor"))
                throw new InvalidDataException("Producto inválido.");
            var price = Number(parts[3], 999999.99m);
            if (price < 0.01m || decimal.Round(price, 2) != price) throw new InvalidDataException("Precio inválido.");
            return new Product(parts[1], parts[2], price);
        }).ToArray();
        if (products.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 90)
            throw new InvalidDataException("Productos duplicados.");
        var ingredients = Lines(Path.Combine(directory, "catalogo-ingredientes.csv"),
            "nombre,unidad,stock_inicial,stock_minimo").Select(line =>
        {
            var parts = line.Split(',');
            if (parts.Length != 4 || !Name(parts[0]) || !Units.Contains(parts[1], StringComparer.Ordinal))
                throw new InvalidDataException("Ingrediente inválido.");
            return new Ingredient(parts[0], parts[1], Number(parts[2], MaxStock, true), Number(parts[3], MaxStock, true));
        }).ToArray();
        if (ingredients.Length != 116 || ingredients.Select(i => i.Name.ToLowerInvariant().Normalize(NormalizationForm.FormC))
            .Distinct(StringComparer.Ordinal).Count() != ingredients.Length)
            throw new InvalidDataException("Ingredientes ausentes o duplicados.");
        var byName = ingredients.ToDictionary(i => i.Name, StringComparer.Ordinal);
        var recipes = new Dictionary<int, Line[]>();
        foreach (var row in File.ReadAllLines(Path.Combine(directory, "recetas.txt"), Encoding.UTF8))
        {
            if (row.Length == 0 || row.StartsWith('#')) continue;
            var fields = row.Split('|');
            if (fields.Length != 2 || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
                number is < 1 or > 90 || recipes.ContainsKey(number)) throw new InvalidDataException("Receta inválida.");
            var details = fields[1].Split(';').Select(item =>
            {
                var parts = item.Split('=');
                if (parts.Length != 2 || !byName.TryGetValue(parts[0], out var ingredient))
                    throw new InvalidDataException("Ingrediente de receta inexistente.");
                var quantity = Number(parts[1], MaxStock);
                if (quantity > ingredient.Stock) throw new InvalidDataException("Stock insuficiente para una porción.");
                return new Line(parts[0], quantity);
            }).ToArray();
            if (details.Length is < 1 or > 50 || details.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != details.Length)
                throw new InvalidDataException("Líneas de receta inválidas.");
            recipes.Add(number, details);
        }
        if (recipes.Count != 90) throw new InvalidDataException("Faltan recetas.");
        return new Data(products, ingredients, recipes);
    }

    private sealed record Product(string Name, string Category, decimal Price);
    private sealed record Ingredient(string Name, string Unit, decimal Stock, decimal Minimum);
    private sealed record Line(string Name, decimal Quantity);
    private sealed record Data(Product[] Products, Ingredient[] Ingredients, Dictionary<int, Line[]> Recipes);
}
