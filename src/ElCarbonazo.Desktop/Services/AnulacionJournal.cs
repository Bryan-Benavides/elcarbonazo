using System.Text.Json;

namespace ElCarbonazo.Desktop.Services;

public sealed record AnulacionJournalState(
    Guid Instalacion,
    string Estado,
    Guid? Operacion,
    int? Propietario);

/// <summary>
/// Durable local barrier for 006. It deliberately stores no reason, credentials, token or request body.
/// The file and lock must be provisioned explicitly with a restrictive Windows ACL.
/// </summary>
public sealed class AnulacionJournal(string path, Guid installation, ICajaRecoverySecurityPolicy security)
{
    private readonly string _lockPath = path + ".lock";

    public AnulacionJournalState? Read() => Locked(ReadLocked);

    public bool Begin(Guid operation, int owner) => Change(current =>
        current.Estado == "SinPendientes" && operation != Guid.Empty && owner > 0
            ? current with { Estado = "Pendiente", Operacion = operation, Propietario = owner }
            : null);

    public bool MarkTerminal(Guid operation, int owner) => Change(current =>
        current.Estado == "Pendiente" && current.Operacion == operation && current.Propietario == owner
            ? current with { Estado = "TerminalPorConciliar" }
            : null);

    public bool ClearAfterReconciliation(Guid operation, int owner) => Change(current =>
        current.Estado == "TerminalPorConciliar" && current.Operacion == operation && current.Propietario == owner
            ? new AnulacionJournalState(installation, "SinPendientes", null, null)
            : null);

    private T? Locked<T>(Func<T?> action)
    {
        try
        {
            if (!Safe(path) || !Safe(_lockPath)) return default;
            using var gate = new FileStream(_lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return default;
        }
    }

    private bool Change(Func<AnulacionJournalState, AnulacionJournalState?> transition)
    {
        string? temporary = null;
        try
        {
            if (!Safe(path) || !Safe(_lockPath)) return false;
            using var gate = new FileStream(_lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var current = ReadLocked();
            if (current is null) return false;
            var next = transition(current);
            if (next is null) return false;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1,
                instalacion = next.Instalacion,
                estado = next.Estado,
                operacion = next.Operacion,
                propietario = next.Propietario
            });
            if (Parse(bytes) != next) return false;
            temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".anulacion-{Guid.NewGuid():N}.tmp");
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            if (!Safe(temporary) || !Safe(path) || !Safe(_lockPath)) return false;
            File.Replace(temporary, path, null);
            temporary = null;
            return Safe(path) && Safe(_lockPath) && ReadLocked() == next;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private AnulacionJournalState? ReadLocked()
    {
        if (!Safe(path)) return null;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > 4096) return null;
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        var state = Parse(bytes);
        return state?.Instalacion == installation ? state : null;
    }

    private AnulacionJournalState? Parse(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) || version.GetInt32() != 1 ||
                !root.TryGetProperty("instalacion", out var install) || install.GetGuid() != installation ||
                !root.TryGetProperty("estado", out var status) || status.GetString() is not ("SinPendientes" or "Pendiente" or "TerminalPorConciliar") ||
                !root.TryGetProperty("operacion", out var operation) ||
                !root.TryGetProperty("propietario", out var owner))
                return null;
            var state = status.GetString()!;
            if (state == "SinPendientes")
                return operation.ValueKind == JsonValueKind.Null && owner.ValueKind == JsonValueKind.Null
                    ? new(installation, state, null, null) : null;
            if (operation.ValueKind != JsonValueKind.String || !operation.TryGetGuid(out var id) || id == Guid.Empty ||
                owner.ValueKind != JsonValueKind.Number || !owner.TryGetInt32(out var actor) || actor <= 0)
                return null;
            return new(installation, state, id, actor);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private bool Safe(string candidate) => File.Exists(candidate) &&
        (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) == 0 && security.IsProtected(candidate);
}
