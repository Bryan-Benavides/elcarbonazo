using System.Globalization;
using System.Text;
using System.Text.Json;
using ElCarbonazo.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed record IngredienteDto(int IdIngrediente, string Nombre, string UnidadMedida, decimal StockActual,
    decimal StockMinimo, bool Activo, string Version, string EstadoAlerta);
public sealed record MovimientoDto(long IdMovimiento, int IdIngrediente, string TipoMovimiento, decimal CantidadDelta,
    decimal StockAnterior, decimal StockPosterior, int? IdPedido, int? IdComanda, int IdUsuario, string? Motivo, DateTimeOffset FechaUtc);
public sealed record ProductoRecetaDto(int IdProducto, string Nombre, string Categoria, bool Disponible, string EstadoReceta);
public sealed record RecetaDetalleLineaDto(int IdIngrediente, string Nombre, string UnidadMedida, decimal CantidadRequerida, bool Activo);
public sealed record RecetaDetalleDto(int IdReceta, int IdProducto, bool Activa, string? NotasPreparacion,
    string Version, IReadOnlyList<RecetaDetalleLineaDto> Lineas);
public sealed record InventarioPagina<T>(IReadOnlyList<T> Items, string? CursorSiguiente);
public sealed record InventarioAlertasDto(int TotalActivos, int StockBajo, int Agotados, InventarioPagina<IngredienteDto> Pagina);

public sealed class InventarioConsultasService(AppDbContext context)
{
    public async Task<InventarioPagina<IngredienteDto>?> IngredientesAsync(string? cursor, int limit, string? state, string? search, CancellationToken ct)
    {
        if (!ValidFilter(search, out var text) || state is not (null or "Normal" or "StockBajo" or "Agotado") ||
            !TryCursor(cursor, "ingredientes", state, text, limit, out var last)) return null;
        var query = context.Ingredientes.AsNoTracking().Where(x => x.IdIngrediente > last);
        if (text is not null) query = query.Where(x => x.NombreClave.Contains(text));
        query = state switch
        {
            "Agotado" => query.Where(x => x.StockActual == 0),
            "StockBajo" => query.Where(x => x.StockActual > 0 && x.StockActual <= x.StockMinimo),
            "Normal" => query.Where(x => x.StockActual > 0 && x.StockActual > x.StockMinimo),
            _ => query
        };
        var rows = await query.OrderBy(x => x.IdIngrediente).Take(limit + 1).ToListAsync(ct);
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new(rows.Select(x => new IngredienteDto(x.IdIngrediente, x.Nombre, x.UnidadMedida, x.StockActual,
            x.StockMinimo, x.Activo, x.Version.ToString(CultureInfo.InvariantCulture),
            x.StockActual == 0 ? "Agotado" : x.StockActual <= x.StockMinimo ? "StockBajo" : "Normal")).ToArray(),
            more ? Encode("ingredientes", rows[^1].IdIngrediente, state, text) : null);
    }

    public async Task<InventarioPagina<MovimientoDto>?> MovimientosAsync(int? ingredientId, string? cursor, int limit, CancellationToken ct)
    {
        var filter = ingredientId?.ToString(CultureInfo.InvariantCulture);
        if (ingredientId is <= 0 || !TryCursor(cursor, "movimientos", filter, null, limit, out var last)) return null;
        var query = context.MovimientosInventario.AsNoTracking().AsQueryable();
        if (ingredientId is not null) query = query.Where(x => x.IdIngrediente == ingredientId);
        if (last > 0) query = query.Where(x => x.IdMovimiento < last);
        var rows = await query.OrderByDescending(x => x.IdMovimiento).Take(limit + 1)
            .Select(x => new MovimientoDto(x.IdMovimiento, x.IdIngrediente, x.TipoMovimiento, x.CantidadDelta, x.StockAnterior,
                x.StockPosterior, x.IdPedido, x.IdComanda, x.IdUsuario, x.Motivo, x.FechaUtc)).ToListAsync(ct);
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new(rows, more ? Encode("movimientos", rows[^1].IdMovimiento, filter, null) : null);
    }

    public async Task<InventarioAlertasDto?> AlertasAsync(string? cursor, int limit, CancellationToken ct)
    {
        if (!TryCursor(cursor, "alertas", null, null, limit, out var last)) return null;
        var active = context.Ingredientes.AsNoTracking().Where(x => x.Activo);
        var total = await active.CountAsync(ct);
        var low = await active.CountAsync(x => x.StockActual > 0 && x.StockActual <= x.StockMinimo, ct);
        var empty = await active.CountAsync(x => x.StockActual == 0, ct);
        var rows = await active.Where(x => x.IdIngrediente > last && (x.StockActual == 0 || x.StockActual <= x.StockMinimo))
            .OrderBy(x => x.IdIngrediente).Take(limit + 1).ToListAsync(ct);
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        var items = rows.Select(x => new IngredienteDto(x.IdIngrediente, x.Nombre, x.UnidadMedida, x.StockActual,
            x.StockMinimo, x.Activo, x.Version.ToString(CultureInfo.InvariantCulture),
            x.StockActual == 0 ? "Agotado" : "StockBajo")).ToArray();
        return new(total, low, empty, new(items, more ? Encode("alertas", rows[^1].IdIngrediente, null, null) : null));
    }

    public async Task<InventarioPagina<ProductoRecetaDto>?> ProductosAsync(string? cursor, int limit, string? search, CancellationToken ct)
    {
        if (!ValidFilter(search, out var text) || !TryCursor(cursor, "receta-productos", null, text, limit, out var last)) return null;
        var query = context.Productos.AsNoTracking().Where(x => x.IdProducto > last);
        if (text is not null) query = query.Where(x => x.Nombre.ToLower().Contains(text));
        var rows = await query.OrderBy(x => x.IdProducto).Take(limit + 1).Select(x => new
        {
            x.IdProducto,
            x.Nombre,
            x.Categoria,
            x.Disponible,
            Recipe = context.Recetas.Where(r => r.IdProducto == x.IdProducto).Select(r => (bool?)r.Activa).FirstOrDefault()
        }).ToListAsync(ct);
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new(rows.Select(x => new ProductoRecetaDto(x.IdProducto, x.Nombre, x.Categoria, x.Disponible,
            x.Recipe is null ? "SinReceta" : x.Recipe.Value ? "Activa" : "Inactiva")).ToArray(),
            more ? Encode("receta-productos", rows[^1].IdProducto, null, text) : null);
    }

    public async Task<(bool Exists, RecetaDetalleDto? Receta)> RecetaAsync(int id, CancellationToken ct)
    {
        if (id <= 0 || !await context.Productos.AsNoTracking().AnyAsync(x => x.IdProducto == id, ct)) return (false, null);
        var recipe = await context.Recetas.AsNoTracking().Include(x => x.Detalles).ThenInclude(x => x.Ingrediente)
            .SingleOrDefaultAsync(x => x.IdProducto == id, ct);
        return (true, recipe is null ? null : new(recipe.IdReceta, recipe.IdProducto, recipe.Activa, recipe.NotasPreparacion,
            recipe.Version.ToString(CultureInfo.InvariantCulture), recipe.Detalles.OrderBy(x => x.IdIngrediente)
                .Select(x => new RecetaDetalleLineaDto(x.IdIngrediente, x.Ingrediente.Nombre, x.Ingrediente.UnidadMedida,
                    x.CantidadRequerida, x.Ingrediente.Activo)).ToArray()));
    }

    private sealed record Cursor(int Version, string Tipo, long Ultimo, string? Filtro, string? Busqueda);
    private static string Encode(string kind, long last, string? filter, string? search) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Cursor(1, kind, last, filter, search)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool TryCursor(string? cursor, string kind, string? filter, string? search, int limit, out long last)
    {
        last = 0;
        if (limit is < 1 or > 100) return false;
        if (cursor is null) return true;
        if (cursor.Length is 0 or > 1024 || cursor.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return false;
        try
        {
            var encoded = cursor.Replace('-', '+').Replace('_', '/');
            var value = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=')));
            if (value is null || value.Version != 1 || value.Tipo != kind || value.Ultimo <= 0 || value.Filtro != filter || value.Busqueda != search) return false;
            last = value.Ultimo;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException) { return false; }
    }
    private static bool ValidFilter(string? input, out string? normalized)
    {
        if (!InventarioService.TryText(input, 120, false, out normalized)) return false;
        normalized = string.IsNullOrEmpty(normalized) ? null : normalized.ToLowerInvariant().Normalize(NormalizationForm.FormC);
        return true;
    }
}
