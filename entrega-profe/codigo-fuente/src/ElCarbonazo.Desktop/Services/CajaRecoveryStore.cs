using System.Text.Json;

namespace ElCarbonazo.Desktop.Services;

// A production implementation must verify Windows ACLs, owner and parent paths.
// No permissive implementation is registered by the application.
public interface ICajaRecoverySecurityPolicy
{
    bool IsProtected(string path);
}

public sealed class CajaRecoveryStore(string path, Guid installation, ICajaRecoverySecurityPolicy security)
{
    private readonly string _lockPath = path + ".lock";

    public CajaRecoveryState? Read()
    {
        try
        {
            if (!Safe(path) || !Safe(_lockPath)) return null;
            using var gate = new FileStream(_lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return ReadLocked();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    public bool Begin(Guid operation, int owner, string type) => Change(state =>
        state.Estado == "SinPendientes" && operation != Guid.Empty && owner > 0 &&
        type is "AbrirTurno" or "CobrarCuenta" or "CerrarTurno"
            ? new CajaRecoveryState(installation, "Pendiente", operation, owner, type) : null);

    public bool MarkTerminal(Guid operation, int owner) => Change(state =>
        state.Estado == "Pendiente" && state.Operacion == operation && state.Propietario == owner
            ? state with { Estado = "TerminalPorConciliar" } : null);

    // Call only after the API returned a terminal result AND the caller reconciled the business state.
    public bool CompleteReconciliation(Guid operation, int owner) => Change(state =>
        state.Estado == "TerminalPorConciliar" && state.Operacion == operation && state.Propietario == owner
            ? new CajaRecoveryState(installation, "SinPendientes", null, null, null) : null);

    private bool Change(Func<CajaRecoveryState, CajaRecoveryState?> transition)
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
                propietario = next.Propietario,
                tipo = next.Tipo
            });
            if (CajaRecoveryJournal.Parse(bytes, installation) is null) return false;
            temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".caja-{Guid.NewGuid():N}.tmp");
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private CajaRecoveryState? ReadLocked()
    {
        if (!Safe(path)) return null;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > 4096) return null;
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        return CajaRecoveryJournal.Parse(bytes, installation);
    }

    private bool Safe(string candidate) => File.Exists(candidate) &&
        (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) == 0 && security.IsProtected(candidate);
}
