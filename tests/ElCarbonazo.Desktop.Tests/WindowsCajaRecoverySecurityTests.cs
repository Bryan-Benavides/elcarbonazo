using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class WindowsCajaRecoverySecurityTests
{
    [Fact]
    public void Rejects_real_file_with_everyone_access_and_unexpected_directory()
    {
        if (!OperatingSystem.IsWindows()) return;
        VerifyWindows();
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyWindows()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"caja-acl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "journal.json");
        try
        {
            File.WriteAllText(path, "{}");
            var info = new FileInfo(path);
            var acl = info.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.ReadData, AccessControlType.Allow));
            info.SetAccessControl(acl);
            var current = WindowsIdentity.GetCurrent().User!;
            var policy = new WindowsCajaRecoverySecurityPolicy(directory, current);
            Assert.False(policy.IsProtected(path));
            Assert.False(new WindowsCajaRecoverySecurityPolicy(Path.GetTempPath(), current).IsProtected(path));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }
}
