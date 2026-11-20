using System.Text.Json;

namespace ElCarbonazo.Desktop.Services;

// Validation only. Storage, Windows ACL verification and atomic replacement are not yet wired.
public sealed record CajaRecoveryState(Guid Instalacion, string Estado, Guid? Operacion, int? Propietario, string? Tipo);

public static class CajaRecoveryJournal
{
    public static CajaRecoveryState? Parse(ReadOnlySpan<byte> json, Guid instalacionEsperada)
    {
        if (instalacionEsperada == Guid.Empty || json.Length is 0 or > 4096) return null;
        try
        {
            using var document = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions { MaxDepth = 3 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name is not ("version" or "instalacion" or "estado" or "operacion" or "propietario" or "tipo") ||
                    !names.Add(property.Name)) return null;
            }
            if (names.Count != 6 || !root.GetProperty("version").TryGetInt32(out var version) || version != 1 ||
                !root.GetProperty("instalacion").TryGetGuid(out var instalacion) || instalacion != instalacionEsperada ||
                root.GetProperty("estado").ValueKind != JsonValueKind.String) return null;
            var state = root.GetProperty("estado").GetString();
            var operation = root.GetProperty("operacion");
            var owner = root.GetProperty("propietario");
            var type = root.GetProperty("tipo");
            if (state == "SinPendientes")
                return operation.ValueKind == JsonValueKind.Null && owner.ValueKind == JsonValueKind.Null && type.ValueKind == JsonValueKind.Null
                    ? new(instalacion, state, null, null, null) : null;
            if (state is not ("Pendiente" or "TerminalPorConciliar") || operation.ValueKind != JsonValueKind.String ||
                !operation.TryGetGuid(out var id) || id == Guid.Empty || owner.ValueKind != JsonValueKind.Number ||
                !owner.TryGetInt32(out var ownerId) || ownerId <= 0 || type.ValueKind != JsonValueKind.String) return null;
            var operationType = type.GetString();
            if (operationType is not ("AbrirTurno" or "CobrarCuenta" or "CerrarTurno")) return null;
            return new(instalacion, state, id, ownerId, operationType);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
