using System.Globalization;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed record RecetaLineaInput(int IdIngrediente, decimal? CantidadRequerida);
public sealed record RecetaInput(bool? Activa, string? NotasPreparacion, string? VersionEsperada, IReadOnlyList<RecetaLineaInput?>? Lineas);

public sealed class RecetasService(AppDbContext context, IAuditRecorder audit, TimeProvider time)
{
    public async Task<InventarioResult> GuardarAsync(int productId, RecetaInput input, int userId, CancellationToken cancellationToken)
    {
        if (productId <= 0 || input.Activa is null || input.Lineas is null || input.Lineas.Count > 50 ||
            (input.Activa.Value && input.Lineas.Count == 0) ||
            input.Lineas.Any(x => x is null || x.IdIngrediente <= 0 || !InventarioService.ValidAmount(x.CantidadRequerida, false)) ||
            input.Lineas.Select(x => x!.IdIngrediente).Distinct().Count() != input.Lineas.Count ||
            !InventarioService.TryText(input.NotasPreparacion, 500, false, out var notes))
            return new(400, "VALIDATION_ERROR");
        if (input.VersionEsperada is not null && !InventarioService.TryVersion(input.VersionEsperada, out _))
            return new(400, "VALIDATION_ERROR");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var product = await context.Productos.FromSqlInterpolated(
            $"SELECT * FROM producto WHERE id_producto = {productId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        if (product is null) return new(404, "HTTP_NO_ENCONTRADO");
        var recipe = await context.Recetas.Include(x => x.Detalles).SingleOrDefaultAsync(x => x.IdProducto == productId, cancellationToken);
        if ((recipe is null && input.VersionEsperada is not null) ||
            (recipe is not null && (input.VersionEsperada is null ||
                !InventarioService.TryVersion(input.VersionEsperada, out var version) || version != recipe.Version || recipe.Version == long.MaxValue)))
            return new(409, "VERSION_CONFLICT");
        var ids = input.Lineas.Select(x => x!.IdIngrediente).Order().ToArray();
        var ingredients = await context.Ingredientes.FromSqlInterpolated($"""
            SELECT * FROM ingrediente WHERE id_ingrediente = ANY({ids}) ORDER BY id_ingrediente FOR UPDATE
            """).ToListAsync(cancellationToken);
        if (ingredients.Count != ids.Length) return new(404, "HTTP_NO_ENCONTRADO");
        if (ingredients.Any(x => !x.Activo)) return new(409, "INGREDIENTE_INACTIVO");
        var now = time.GetUtcNow();
        if (recipe is null)
        {
            recipe = new Receta { IdProducto = productId, FechaActualizacionUtc = now };
            context.Recetas.Add(recipe);
        }
        else
        {
            context.DetallesReceta.RemoveRange(recipe.Detalles);
            // Remove old unique keys before inserting replacement lines, within the same transaction.
            await context.SaveChangesAsync(cancellationToken);
            recipe.Version++;
        }
        recipe.Activa = input.Activa.Value;
        recipe.NotasPreparacion = notes;
        recipe.FechaActualizacionUtc = now;
        foreach (var line in input.Lineas)
        {
            context.DetallesReceta.Add(new DetalleReceta
            {
                Receta = recipe,
                IdIngrediente = line!.IdIngrediente,
                CantidadRequerida = line.CantidadRequerida!.Value
            });
        }
        await context.SaveChangesAsync(cancellationToken);
        audit.Record("RecetaGuardada", "Exitoso", userId, entityType: "receta",
            entityId: recipe.IdReceta.ToString(CultureInfo.InvariantCulture));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(200, Id: recipe.IdReceta);
    }
}
