using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace ElCarbonazo.Desktop.Services;

/// <summary>
/// Explicit first-install provisioning. Never invoked during startup and never repairs an existing journal.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsAnulacionJournalProvisioner
{
    public static bool CreateFresh(string directory, Guid installation)
    {
        if (installation == Guid.Empty) return false;
        try
        {
            var full = Path.GetFullPath(directory);
            if (Directory.Exists(full) || File.Exists(full)) return false;
            var parent = Directory.GetParent(full);
            if (parent is null || !parent.Exists) return false;
            var current = WindowsIdentity.GetCurrent().User;
            if (current is null) return false;

            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[]
                     {
                         current,
                         new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                         new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)
                     })
                acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(full).Create(acl);

            var path = Path.Combine(full, "anulaciones-journal.json");
            using (new FileStream(path + ".lock", FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            var policy = new WindowsCajaRecoverySecurityPolicy(full, current);
            if (!policy.IsProtected(path + ".lock")) return false;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1,
                instalacion = installation,
                estado = "SinPendientes",
                operacion = (Guid?)null,
                propietario = (int?)null
            });
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            return new AnulacionJournal(path, installation, policy).Read()?.Estado == "SinPendientes";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
