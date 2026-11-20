using System.Net.Http.Json;
using System.Text.Json;
using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Services;

public sealed record IngredienteLectura(int Id, string Nombre, string Unidad, decimal Stock, decimal Minimo, bool Activo, string Estado, string Version = "")
{
    public string Texto => $"{Nombre} · {Stock:0.####} {Unidad} · mínimo {Minimo:0.####} · {Estado}" + (Activo ? "" : " · Inactivo");
}

public sealed record ProductoRecetaLectura(int Id, string Nombre, string Estado)
{
    public string Texto => $"{Nombre} · {Estado}";
}

public sealed record InventarioPaginaLectura<T>(IReadOnlyList<T> Items, string? Siguiente);
public sealed record ResumenAlertasLectura(int TotalActivos, int StockBajo, int Agotados);
public sealed record LineaRecetaLectura(int Id, string Nombre, string Unidad, decimal Cantidad, bool Activo)
{
    public string Texto => $"{Nombre}: {Cantidad:0.####} {Unidad}" + (Activo ? "" : " · Inactivo");
}
public sealed record RecetaLectura(int ProductoId, bool Existe, bool Activa, string? Version, string? Notas, IReadOnlyList<LineaRecetaLectura> Lineas);
public sealed record MovimientoLectura(long Id, string Tipo, decimal Delta, decimal Anterior, decimal Posterior,
    int IdUsuario, DateTimeOffset Fecha, string? Motivo)
{
    public string Texto => $"{new ServerClock().ToManaguaTime(Fecha):yyyy-MM-dd HH:mm} · {Tipo} · {Delta:0.####} · {Anterior:0.####} → {Posterior:0.####} · usuario {IdUsuario}" +
        (string.IsNullOrEmpty(Motivo) ? "" : $" · {Motivo}");
}

public interface IInventarioLecturaService
{
    Task<InventarioPaginaLectura<IngredienteLectura>> IngredientesAsync(string? cursor, CancellationToken ct);
    Task<InventarioPaginaLectura<ProductoRecetaLectura>> ProductosAsync(string? cursor, CancellationToken ct);
    Task<InventarioPaginaLectura<ProductoRecetaLectura>> ProductosFiltradosAsync(string? cursor, string? busqueda, CancellationToken ct);
    Task<string> RecetaAsync(int productoId, CancellationToken ct);
    Task<ResumenAlertasLectura> AlertasAsync(CancellationToken ct);
    Task<InventarioPaginaLectura<IngredienteLectura>> IngredientesFiltradosAsync(string? cursor, string? estado, string? busqueda, CancellationToken ct);
    Task<InventarioPaginaLectura<MovimientoLectura>> MovimientosAsync(int ingredientId, string? cursor, CancellationToken ct);
    Task<RecetaLectura> DetalleRecetaAsync(int productoId, CancellationToken ct);
}

public sealed class InventarioLecturaService(ISessionManager session) : IInventarioLecturaService
{
    public async Task<ResumenAlertasLectura> AlertasAsync(CancellationToken ct)
    {
        var root = await ReadAsync("api/inventario/alertas?limite=25", ct);
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("totalActivos", out var total) || !total.TryGetInt32(out var totalValue) || totalValue < 0 ||
            !root.TryGetProperty("stockBajo", out var low) || !low.TryGetInt32(out var lowValue) || lowValue < 0 ||
            !root.TryGetProperty("agotados", out var empty) || !empty.TryGetInt32(out var emptyValue) || emptyValue < 0 ||
            (long)lowValue + emptyValue > totalValue || !root.TryGetProperty("pagina", out var page))
            throw new InvalidDataException();
        _ = Page(page);
        return new(totalValue, lowValue, emptyValue);
    }

    public async Task<InventarioPaginaLectura<IngredienteLectura>> IngredientesAsync(string? cursor, CancellationToken ct)
        => await IngredientesFiltradosAsync(cursor, null, null, ct);

    public async Task<InventarioPaginaLectura<IngredienteLectura>> IngredientesFiltradosAsync(string? cursor, string? estado, string? busqueda, CancellationToken ct)
    {
        if (estado is not (null or "Normal" or "StockBajo" or "Agotado") || busqueda?.Length > 120) throw new InvalidDataException();
        var root = await ReadAsync("api/inventario/ingredientes?limite=25" + Cursor(cursor) +
            (estado is null ? "" : $"&estadoAlerta={Uri.EscapeDataString(estado)}") +
            (string.IsNullOrWhiteSpace(busqueda) ? "" : $"&busqueda={Uri.EscapeDataString(busqueda)}"), ct);
        var (items, next) = Page(root);
        var result = new List<IngredienteLectura>();
        foreach (var item in items.EnumerateArray())
        {
            var id = PositiveInt(item, "idIngrediente");
            var name = Text(item, "nombre", 120);
            var unit = Text(item, "unidadMedida", 20);
            if (unit is not ("lb" or "kg" or "oz" or "g" or "unidad" or "ml" or "lt")) throw new InvalidDataException();
            var stock = Amount(item, "stockActual");
            var minimum = Amount(item, "stockMinimo");
            if (!item.TryGetProperty("activo", out var active) || active.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException();
            var state = Text(item, "estadoAlerta", 20);
            var version = Text(item, "version", 19);
            if (!long.TryParse(version, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedVersion) ||
                parsedVersion <= 0 || version != parsedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidDataException();
            var expected = stock == 0 ? "Agotado" : stock <= minimum ? "StockBajo" : "Normal";
            if (state != expected) throw new InvalidDataException();
            result.Add(new(id, name, unit, stock, minimum, active.GetBoolean(), state, version));
        }
        if (result.Select(x => x.Id).Distinct().Count() != result.Count) throw new InvalidDataException();
        return new(result, next);
    }

    public async Task<InventarioPaginaLectura<MovimientoLectura>> MovimientosAsync(int ingredientId, string? cursor, CancellationToken ct)
    {
        if (ingredientId <= 0) throw new InvalidDataException();
        var root = await ReadAsync($"api/inventario/movimientos?idIngrediente={ingredientId}&limite=25" + Cursor(cursor), ct);
        var (items, next) = Page(root);
        var result = new List<MovimientoLectura>();
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("idMovimiento", out var id) || !id.TryGetInt64(out var number) || number <= 0 ||
                PositiveInt(item, "idIngrediente") != ingredientId) throw new InvalidDataException();
            var type = Text(item, "tipoMovimiento", 30);
            if (type is not ("EntradaCompra" or "AjustePositivo" or "AjusteNegativo" or "ConsumoPedido")) throw new InvalidDataException();
            if (!item.TryGetProperty("cantidadDelta", out var deltaValue) || !deltaValue.TryGetDecimal(out var delta) ||
                delta == 0 || Math.Abs(delta) > 99999999.9999m || decimal.Round(delta, 4) != delta ||
                ((type is "EntradaCompra" or "AjustePositivo") != (delta > 0))) throw new InvalidDataException();
            var before = Amount(item, "stockAnterior");
            var after = Amount(item, "stockPosterior");
            if (before + delta != after || !item.TryGetProperty("fechaUtc", out var date) || !date.TryGetDateTimeOffset(out var timestamp) ||
                timestamp.Offset != TimeSpan.Zero || !item.TryGetProperty("motivo", out var reason) ||
                reason.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) throw new InvalidDataException();
            var motive = reason.ValueKind == JsonValueKind.Null ? null : reason.GetString();
            if (motive?.Length > 500 || motive?.Any(char.IsControl) == true ||
                (type is "AjustePositivo" or "AjusteNegativo" && string.IsNullOrWhiteSpace(motive))) throw new InvalidDataException();
            result.Add(new(number, type, delta, before, after, PositiveInt(item, "idUsuario"), timestamp, motive));
        }
        if (result.Select(x => x.Id).Distinct().Count() != result.Count) throw new InvalidDataException();
        return new(result, next);
    }

    public async Task<InventarioPaginaLectura<ProductoRecetaLectura>> ProductosAsync(string? cursor, CancellationToken ct)
        => await ProductosFiltradosAsync(cursor, null, ct);

    public async Task<InventarioPaginaLectura<ProductoRecetaLectura>> ProductosFiltradosAsync(string? cursor, string? busqueda, CancellationToken ct)
    {
        if (busqueda?.Length > 120 || busqueda?.Any(char.IsControl) == true) throw new InvalidDataException();
        var root = await ReadAsync("api/recetas/productos?limite=25" + Cursor(cursor) +
            (string.IsNullOrWhiteSpace(busqueda) ? "" : $"&busqueda={Uri.EscapeDataString(busqueda.Trim())}"), ct);
        var (items, next) = Page(root);
        var result = new List<ProductoRecetaLectura>();
        foreach (var item in items.EnumerateArray())
        {
            var state = Text(item, "estadoReceta", 20);
            if (state is not ("SinReceta" or "Activa" or "Inactiva")) throw new InvalidDataException();
            result.Add(new(PositiveInt(item, "idProducto"), Text(item, "nombre", 120), state));
        }
        if (result.Select(x => x.Id).Distinct().Count() != result.Count) throw new InvalidDataException();
        return new(result, next);
    }

    public async Task<string> RecetaAsync(int productoId, CancellationToken ct)
    {
        var detail = await DetalleRecetaAsync(productoId, ct);
        return !detail.Existe ? "Producto sin receta configurada." :
            string.Join(Environment.NewLine, new[] { detail.Activa ? "Receta activa" : "Receta inactiva" }.Concat(detail.Lineas.Select(x => x.Texto)));
    }

    public async Task<RecetaLectura> DetalleRecetaAsync(int productoId, CancellationToken ct)
    {
        if (productoId <= 0) throw new InvalidDataException();
        var root = await ReadAsync($"api/recetas/producto/{productoId}", ct);
        if (!root.TryGetProperty("receta", out var recipe)) throw new InvalidDataException();
        if (recipe.ValueKind == JsonValueKind.Null) return new(productoId, false, false, null, null, []);
        if (recipe.ValueKind != JsonValueKind.Object || PositiveInt(recipe, "idProducto") != productoId ||
            !recipe.TryGetProperty("activa", out var active) || active.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !recipe.TryGetProperty("lineas", out var lines) || lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() > 50)
            throw new InvalidDataException();
        var version = Text(recipe, "version", 19);
        if (!long.TryParse(version, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedVersion) ||
            parsedVersion <= 0 || version != parsedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            !recipe.TryGetProperty("notasPreparacion", out var notes) || notes.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            throw new InvalidDataException();
        var note = notes.ValueKind == JsonValueKind.Null ? null : notes.GetString();
        if (note?.Length > 1000 || note?.Any(char.IsControl) == true) throw new InvalidDataException();
        var entries = new List<LineaRecetaLectura>();
        foreach (var line in lines.EnumerateArray())
        {
            var id = PositiveInt(line, "idIngrediente");
            var name = Text(line, "nombre", 120);
            var unit = Text(line, "unidadMedida", 20);
            if (unit is not ("lb" or "kg" or "oz" or "g" or "unidad" or "ml" or "lt")) throw new InvalidDataException();
            var quantity = Amount(line, "cantidadRequerida");
            if (quantity <= 0) throw new InvalidDataException();
            if (!line.TryGetProperty("activo", out var ingredientActive) ||
                ingredientActive.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException();
            entries.Add(new(id, name, unit, quantity, ingredientActive.GetBoolean()));
        }
        if ((active.GetBoolean() && lines.GetArrayLength() == 0) || entries.Select(x => x.Id).Distinct().Count() != entries.Count)
            throw new InvalidDataException();
        return new(productoId, true, active.GetBoolean(), version, note, entries);
    }

    private async Task<JsonElement> ReadAsync(string path, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var response = await session.SendPollingAsync(() => new HttpRequestMessage(HttpMethod.Get, path), timeout.Token);
        await MesaApiResponse.EnsureSuccessAsync(response, timeout.Token);
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: timeout.Token);
    }

    private static (JsonElement Items, string? Next) Page(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 25 ||
            !root.TryGetProperty("cursorSiguiente", out var next) || next.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            throw new InvalidDataException();
        var cursor = next.ValueKind == JsonValueKind.Null ? null : next.GetString();
        if (cursor is not null && (cursor.Length is 0 or > 1024 || cursor.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')))
            throw new InvalidDataException();
        return (items, cursor);
    }

    private static string Cursor(string? value) => value is null ? "" : $"&cursor={Uri.EscapeDataString(value)}";
    private static int PositiveInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) && number > 0 ? number : throw new InvalidDataException();
    private static string Text(JsonElement element, string property, int max) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        value.GetString() is { } text && text.Length is > 0 && text.Length <= max * 2 && !text.Any(char.IsControl) ? text : throw new InvalidDataException();
    private static decimal Amount(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetDecimal(out var number) &&
        number >= 0 && number <= 99999999.9999m && decimal.Round(number, 4) == number ? number : throw new InvalidDataException();
}
