using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ElCarbonazo.Desktop.Services;

// Fail-closed policy. It is deliberately not registered until installer ACLs are verified.
[SupportedOSPlatform("windows")]
public sealed class WindowsCajaRecoverySecurityPolicy : ICajaRecoverySecurityPolicy
{
    private readonly string _directory;
    private readonly HashSet<SecurityIdentifier> _trusted;
    private const FileSystemRights Dangerous = FileSystemRights.WriteData | FileSystemRights.AppendData |
        FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes | FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    public WindowsCajaRecoverySecurityPolicy(string directory, SecurityIdentifier operatorSid)
    {
        _directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        _trusted = [operatorSid, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)];
    }

    public bool IsProtected(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(full), _directory, StringComparison.OrdinalIgnoreCase)) return false;
            var file = new FileInfo(full);
            var directory = new DirectoryInfo(_directory);
            if (!file.Exists || !directory.Exists || IsLink(file) || IsLink(directory)) return false;
            if (!CheckAcl(file.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner), true) ||
                !CheckAcl(directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner), true)) return false;
            for (var parent = directory.Parent; parent is not null; parent = parent.Parent)
            {
                if (IsLink(parent) || !CheckAcl(parent.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner), false))
                    return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private bool CheckAcl(FileSystemSecurity acl, bool privateObject)
    {
        if (privateObject && (acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !_trusted.Contains(owner))) return false;
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (rule.IdentityReference is not SecurityIdentifier sid) return false;
            if (!_trusted.Contains(sid) && (privateObject || (rule.FileSystemRights & Dangerous) != 0)) return false;
        }
        return true;
    }

    private static bool IsLink(FileSystemInfo info) => (info.Attributes & FileAttributes.ReparsePoint) != 0;
}
