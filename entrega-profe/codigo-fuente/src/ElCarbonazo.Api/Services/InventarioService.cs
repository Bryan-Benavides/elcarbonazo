using System.Globalization;
using System.Text;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ElCarbonazo.Api.Services;

public sealed record IngredienteInput(string? Nombre, string? UnidadMedida, decimal? StockMinimo, bool? Activo, string? Version);
public sealed record MovimientoInput(int IdIngrediente, string? TipoMovimiento, decimal? Cantidad, string? Motivo);
public sealed record InventarioResult(int Status, string? Code = null, int? Id = null);

public sealed class InventarioService(AppDbContext context, IAuditRecorder audit, TimeProvider time)
{
    private const decimal Max = 99999999.9999m;
    private static readonly string[] Units = ["lb", "kg", "oz", "g", "unidad", "ml", "lt"];

    public async Task<InventarioResult> CrearIngredienteAsync(IngredienteInput input, int userId, CancellationToken cancellationToken)
    {
        if (!TryName(input.Nombre, out var name, out var key) ||
            !Units.Contains(input.UnidadMedida, StringComparer.Ordinal) ||
            !ValidAmount(input.StockMinimo, allowZero: true) || input.Activo is not null || input.Version is not null)
            return new(400, "VALIDATION_ERROR");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var now = time.GetUtcNow();
        var ingredient = new Ingrediente
        {
            Nombre = name,
            NombreClave = key,
            UnidadMedida = input.UnidadMedida!,
            StockMinimo = input.StockMinimo!.Value,
            FechaCreacionUtc = now,
            FechaActualizacionUtc = now
        };
        context.Ingredientes.Add(ingredient);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            audit.Record("IngredienteCreado", "Exitoso", userId, entityType: "ingrediente",
                entityId: ingredient.IdIngrediente.ToString(CultureInfo.InvariantCulture));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(201, Id: ingredient.IdIngrediente);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_ingrediente_nombre_clave" })
        {
            return new(409, "INGREDIENTE_DUPLICADO");
        }
    }

    public async Task<InventarioResult> ActualizarIngredienteAsync(int id, IngredienteInput input, int userId, CancellationToken cancellationToken)
    {
        if (id <= 0 || !TryName(input.Nombre, out var name, out var key) ||
            !Units.Contains(input.UnidadMedida, StringComparer.Ordinal) ||
            !ValidAmount(input.StockMinimo, true) || input.Activo is null ||
            !TryVersion(input.Version, out var version))
            return new(400, "VALIDATION_ERROR");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var ingredient = await context.Ingredientes.FromSqlInterpolated(
            $"SELECT * FROM ingrediente WHERE id_ingrediente = {id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        if (ingredient is null) return new(404, "HTTP_NO_ENCONTRADO");
        if (ingredient.Version != version || ingredient.Version == long.MaxValue) return new(409, "VERSION_CONFLICT");
        if (ingredient.UnidadMedida != input.UnidadMedida &&
            (await context.MovimientosInventario.AnyAsync(x => x.IdIngrediente == id, cancellationToken) ||
             await context.DetallesReceta.AnyAsync(x => x.IdIngrediente == id, cancellationToken)))
            return new(409, "UNIDAD_EN_USO");
        if (!input.Activo.Value && await context.DetallesReceta.AnyAsync(
                x => x.IdIngrediente == id && x.Receta.Activa, cancellationToken))
            return new(409, "INGREDIENTE_EN_USO");
        ingredient.Nombre = name;
        ingredient.NombreClave = key;
        ingredient.UnidadMedida = input.UnidadMedida!;
        ingredient.StockMinimo = input.StockMinimo!.Value;
        ingredient.Activo = input.Activo.Value;
        ingredient.FechaActualizacionUtc = time.GetUtcNow();
        ingredient.Version++;
        audit.Record("IngredienteActualizado", "Exitoso", userId, entityType: "ingrediente",
            entityId: id.ToString(CultureInfo.InvariantCulture));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(200, Id: id);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_ingrediente_nombre_clave" })
        {
            return new(409, "INGREDIENTE_DUPLICADO");
        }
    }

    public async Task<InventarioResult> RegistrarMovimientoAsync(MovimientoInput input, int userId, CancellationToken cancellationToken)
    {
        if (input.IdIngrediente <= 0 || input.TipoMovimiento is not ("EntradaCompra" or "AjustePositivo" or "AjusteNegativo") ||
            !ValidAmount(input.Cantidad, false) ||
            !TryText(input.Motivo, 250, input.TipoMovimiento != "EntradaCompra", out var reason))
            return new(400, "VALIDATION_ERROR");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var ingredient = await context.Ingredientes.FromSqlInterpolated(
            $"SELECT * FROM ingrediente WHERE id_ingrediente = {input.IdIngrediente} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        if (ingredient is null) return new(404, "HTTP_NO_ENCONTRADO");
        if (!ingredient.Activo) return new(409, "INGREDIENTE_INACTIVO");
        if (ingredient.Version == long.MaxValue) return new(409, "VERSION_CONFLICT");
        var delta = input.TipoMovimiento == "AjusteNegativo" ? -input.Cantidad!.Value : input.Cantidad!.Value;
        var next = ingredient.StockActual + delta;
        if (next < 0) return new(409, "STOCK_INSUFICIENTE");
        if (next > Max) return new(400, "VALIDATION_ERROR");
        var now = time.GetUtcNow();
        var movement = new MovimientoInventario
        {
            IdIngrediente = ingredient.IdIngrediente,
            TipoMovimiento = input.TipoMovimiento!,
            CantidadDelta = delta,
            StockAnterior = ingredient.StockActual,
            StockPosterior = next,
            IdUsuario = userId,
            Motivo = reason,
            FechaUtc = now
        };
        ingredient.StockActual = next;
        ingredient.Version++;
        ingredient.FechaActualizacionUtc = now;
        context.MovimientosInventario.Add(movement);
        await context.SaveChangesAsync(cancellationToken);
        audit.Record("MovimientoInventarioRegistrado", "Exitoso", userId, entityType: "movimiento_inventario",
            entityId: movement.IdMovimiento.ToString(CultureInfo.InvariantCulture));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(201, Id: ingredient.IdIngrediente);
    }

    internal static bool ValidAmount(decimal? value, bool allowZero) =>
        value is not null && (allowZero ? value >= 0 : value > 0) && value <= Max && decimal.Round(value.Value, 4) == value;

    internal static bool TryVersion(string? input, out long version) =>
        long.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version > 0 &&
        input == version.ToString(CultureInfo.InvariantCulture);

    internal static bool TryText(string? input, int limit, bool required, out string? output)
    {
        output = null;
        if (input is null) return !required;
        try { output = input.Normalize(NormalizationForm.FormC).Trim(); }
        catch (ArgumentException) { return false; }
        return (!required || output.Length > 0) && output.EnumerateRunes().Count() <= limit &&
            !output.EnumerateRunes().Any(Rune.IsControl);
    }

    private static bool TryName(string? input, out string name, out string key)
    {
        name = string.Empty;
        key = string.Empty;
        if (!TryText(input, 120, true, out var normalized)) return false;
        name = normalized!;
        key = name.ToLowerInvariant().Normalize(NormalizationForm.FormC);
        return key.EnumerateRunes().Count() <= 240;
    }
}
