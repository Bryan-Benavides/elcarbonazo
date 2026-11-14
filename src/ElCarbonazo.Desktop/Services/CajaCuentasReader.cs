using System.Text.Json;

namespace ElCarbonazo.Desktop.Services;

public sealed record CajaCuentaLineaVista(string Texto);
public sealed record CajaCuentaVista(int IdPedido, string Destino, string Estado, decimal Total, bool Cobrable,
    string Explicacion, IReadOnlyList<CajaCuentaLineaVista> Detalle, string? HuellaLineas = null);
public sealed record CajaCuentasPaginaVista(IReadOnlyList<CajaCuentaVista> Items, string? CursorSiguiente);

public interface ICajaCuentasReader
{
    Task<CajaCuentasPaginaVista?> ReadAsync(int actor, string? cursor, CancellationToken cancellationToken);
}

public sealed class CajaCuentasReader(ICajaSessionSender session) : ICajaCuentasReader
{
    public async Task<CajaCuentasPaginaVista?> ReadAsync(int actor, string? cursor, CancellationToken cancellationToken)
    {
        if (actor <= 0 || (cursor is not null && !ValidCursor(cursor))) return null;
        var path = "api/caja/cuentas?limite=25" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
        using var response = await session.SendCajaOnceAsync(actor, () => new HttpRequestMessage(HttpMethod.Get, path), true, cancellationToken);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1048576) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[1048577];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        if (count > 1048576) return null;
        using var document = JsonDocument.Parse(buffer.AsMemory(0, count));
        var root = document.RootElement;
        if (!Object(root) || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 25 ||
            !root.TryGetProperty("cursorSiguiente", out var next) || next.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return null;
        var nextCursor = next.ValueKind == JsonValueKind.Null ? null : next.GetString();
        if (nextCursor is not null && (!ValidCursor(nextCursor) || nextCursor == cursor || items.GetArrayLength() == 0)) return null;
        var result = new List<CajaCuentaVista>();
        var previous = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (!Object(item) || !Int(item, "idPedido", out var id) || id <= previous ||
                !NullableInt(item, "idMesa", out var table) || !NullableInt(item, "numeroMesa", out var number) ||
                !NullableInt(item, "idGrupo", out var group) || (table is null) == (group is null) ||
                (table is not null && (number is null || number <= 0)) || (table is null && number is not null) ||
                !Text(item, "estado", out var state) || !Money(item, "subtotal", out var accountSubtotal) ||
                !Money(item, "impuestos", out var taxes) || !Money(item, "total", out var total) ||
                !item.TryGetProperty("cobrable", out var eligible) || eligible.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !item.TryGetProperty("motivo", out var reason) || reason.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) ||
                !item.TryGetProperty("comandas", out var commands) || commands.ValueKind != JsonValueKind.Array) return null;
            var motive = reason.ValueKind == JsonValueKind.Null ? null : reason.GetString();
            if (eligible.GetBoolean() ? motive is not null : string.IsNullOrWhiteSpace(motive)) return null;
            if (!item.TryGetProperty("huellaLineas", out var hashElement) || hashElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return null;
            var hash = hashElement.ValueKind == JsonValueKind.String ? hashElement.GetString() : null;
            if (eligible.GetBoolean() && (state != "Listo" || total <= 0 || hash is null || hash.Length != 64 ||
                taxes != 0 || accountSubtotal != total || hash.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))) return null;
            var details = new List<CajaCuentaLineaVista>();
            decimal lineSum = 0;
            foreach (var command in commands.EnumerateArray())
            {
                if (!Object(command) || !Int(command, "idComanda", out var commandId) || commandId <= 0 ||
                    !Text(command, "estado", out var commandState) || !command.TryGetProperty("version", out var version) ||
                    version.ValueKind != JsonValueKind.Number || !version.TryGetUInt32(out _) ||
                    !command.TryGetProperty("lineas", out var lines) || lines.ValueKind != JsonValueKind.Array) return null;
                if (eligible.GetBoolean() && (commandState != "Lista" || lines.GetArrayLength() == 0)) return null;
                details.Add(new CajaCuentaLineaVista($"Comanda {commandId} · {commandState}"));
                foreach (var line in lines.EnumerateArray())
                {
                    if (!Object(line) || !Int(line, "idDetalle", out var detailId) || detailId <= 0 ||
                        !Int(line, "idProducto", out var productId) || productId <= 0 || !Text(line, "nombreProducto", out var name) || string.IsNullOrWhiteSpace(name) ||
                        !Int(line, "cantidad", out var quantity) || quantity <= 0 || !Money(line, "precioUnitario", out var price) ||
                        !Money(line, "subtotal", out var subtotal)) return null;
                    if (eligible.GetBoolean() && (price <= 0 || quantity > 99 || price * quantity != subtotal)) return null;
                    lineSum += subtotal;
                    if (lineSum > 9999999999.99m) return null;
                    details.Add(new CajaCuentaLineaVista($"{quantity} × {name} · C$ {price:N2} · subtotal C$ {subtotal:N2}"));
                }
            }
            if (eligible.GetBoolean() && (commands.GetArrayLength() == 0 || details.Count <= commands.GetArrayLength() || lineSum != total)) return null;
            previous = id;
            result.Add(new CajaCuentaVista(id, table is not null ? $"Mesa {number:00}" : $"Grupo {group}", state!, total,
                eligible.GetBoolean(), eligible.GetBoolean() ? "Cobrable" : Explain(motive!), details, hash));
        }
        return new CajaCuentasPaginaVista(result, nextCursor);
    }

    private static bool Object(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() == value.EnumerateObject().Count();
    private static bool Int(JsonElement value, string name, out int result)
    {
        result = 0;
        return value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out result);
    }
    private static bool NullableInt(JsonElement value, string name, out int? result)
    {
        result = null;
        if (!value.TryGetProperty(name, out var property)) return false;
        if (property.ValueKind == JsonValueKind.Null) return true;
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var number) || number <= 0) return false;
        result = number;
        return true;
    }
    private static bool Text(JsonElement value, string name, out string? result)
    {
        result = null;
        if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return false;
        result = property.GetString();
        return !string.IsNullOrWhiteSpace(result);
    }
    private static bool Money(JsonElement value, string name, out decimal result)
    {
        result = 0;
        return value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out result) &&
            result >= 0 && result <= 9999999999.99m && decimal.Round(result, 2) == result;
    }
    private static bool ValidCursor(string cursor) => cursor.Length is > 0 and <= 256 &&
        cursor.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static string Explain(string reason) => reason switch
    {
        "DESTINO_INVALIDO" => "Destino no disponible; requiere revisión.",
        "CUENTA_YA_PAGADA" => "Cuenta ya pagada.",
        "CUENTA_CANCELADA" => "Cuenta cancelada.",
        "CUENTA_LEGADA_SIN_COMANDAS" => "Cuenta anterior sin comandas; requiere revisión.",
        "COMANDAS_PENDIENTES" => "Hay comandas pendientes de cocina.",
        "CUENTA_SIN_DETALLE" => "La cuenta no tiene detalle verificable.",
        "DETALLE_INCONSISTENTE" or "TOTAL_INCONSISTENTE" => "El detalle o total requiere revisión.",
        "TOTAL_NO_COBRABLE" => "El total no es cobrable.",
        "OPERACION_NO_TERMINAL" => "Hay una operación de cobro pendiente de resolver.",
        _ => $"No cobrable: {reason}."
    };
}
