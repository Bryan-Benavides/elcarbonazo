using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class WindowsCajaRecoverySecurityTests
{
    [Fact]
    public void First_install_reads_configuration_and_preserves_existing_pending_journal()
    {
        if (!OperatingSystem.IsWindows()) return;
        VerifyFirstInstall();
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyFirstInstall()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode");
        var directory = Path.Combine(root, $"caja-install-{Guid.NewGuid():N}");
        var recovery = Path.Combine(directory, "recovery");
        var journal = Path.Combine(recovery, "caja-journal.json");
        var configuration = Path.Combine(directory, "desktop.json");
        var installation = Guid.NewGuid();
        Directory.CreateDirectory(directory);
        try
        {
            Assert.False(CajaFirstInstall.Create(directory));
            Assert.False(Directory.Exists(recovery));
            File.WriteAllText(configuration, JsonSerializer.Serialize(new { apiBaseUrl = "https://localhost:7191", idInstalacion = installation }));
            Assert.True(CajaFirstInstall.Create(directory));
            var policy = new WindowsCajaRecoverySecurityPolicy(recovery, WindowsIdentity.GetCurrent().User!);
            var store = new CajaRecoveryStore(journal, installation, policy);
            Assert.Equal("SinPendientes", store.Read()!.Estado);
            var operation = Guid.NewGuid();
            Assert.True(store.Begin(operation, 1, "CobrarCuenta"));
            Assert.False(CajaFirstInstall.Create(directory));
            Assert.Equal(operation, store.Read()!.Operacion);
            Assert.Null(new CajaRecoveryStore(journal, Guid.NewGuid(), policy).Read());
            using var json = JsonDocument.Parse(File.ReadAllBytes(journal));
            Assert.Equal(new[] { "estado", "instalacion", "operacion", "propietario", "tipo", "version" },
                json.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToArray());
        }
        finally
        {
            File.Delete(journal);
            File.Delete(journal + ".lock");
            if (Directory.Exists(recovery)) Directory.Delete(recovery);
            File.Delete(configuration);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void Fresh_provisioning_is_explicit_and_never_overwrites_existing_journal()
    {
        if (!OperatingSystem.IsWindows()) return;
        VerifyProvisioning();
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyProvisioning()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode");
        var directory = Path.Combine(root, $"caja-provision-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "caja-journal.json");
        var installation = Guid.NewGuid();
        try
        {
            Assert.True(WindowsCajaJournalProvisioner.CreateFresh(directory, installation));
            var store = new CajaRecoveryStore(path, installation, new WindowsCajaRecoverySecurityPolicy(directory, WindowsIdentity.GetCurrent().User!));
            var operation = Guid.NewGuid();
            Assert.True(store.Begin(operation, 1, "AbrirTurno"));
            Assert.False(WindowsCajaJournalProvisioner.CreateFresh(directory, installation));
            Assert.Equal(operation, store.Read()!.Operacion);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    [Fact]
    public void Protected_temporary_journal_can_be_replaced_without_weakening_acl()
    {
        if (!OperatingSystem.IsWindows()) return;
        VerifyProtectedJournal();
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyProtectedJournal()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode");
        var directory = Path.Combine(root, $"caja-acl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "journal.json");
        var installation = Guid.NewGuid();
        var operatorSid = WindowsIdentity.GetCurrent().User!;
        try
        {
            var directoryInfo = new DirectoryInfo(directory);
            var acl = directoryInfo.GetAccessControl();
            acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { operatorSid, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                         new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directoryInfo.SetAccessControl(acl);
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1,
                instalacion = installation,
                estado = "SinPendientes",
                operacion = (Guid?)null,
                propietario = (int?)null,
                tipo = (string?)null
            }));
            File.WriteAllText(path + ".lock", "");
            var policy = new WindowsCajaRecoverySecurityPolicy(directory, operatorSid);
            Assert.True(policy.IsProtected(path));
            Assert.True(policy.IsProtected(path + ".lock"));
            var store = new CajaRecoveryStore(path, installation, policy);
            var id = Guid.NewGuid();
            Assert.True(store.Begin(id, 1, "AbrirTurno"));
            Assert.True(policy.IsProtected(path));
            Assert.Equal(id, store.Read()!.Operacion);
            Assert.True(store.MarkTerminal(id, 1));
            Assert.True(policy.IsProtected(path));
            Assert.True(store.CompleteReconciliation(id, 1));
            Assert.True(policy.IsProtected(path));
            var file = new FileInfo(path);
            var insecureAcl = file.GetAccessControl();
            var everyone = new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.ReadData, AccessControlType.Allow);
            insecureAcl.AddAccessRule(everyone);
            file.SetAccessControl(insecureAcl);
            Assert.False(policy.IsProtected(path));
            Assert.Null(store.Read());
            Assert.False(store.Begin(Guid.NewGuid(), 1, "AbrirTurno"));
            insecureAcl.RemoveAccessRuleSpecific(everyone);
            file.SetAccessControl(insecureAcl);
            Assert.True(policy.IsProtected(path));
            Assert.Equal("SinPendientes", store.Read()!.Estado);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

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
            Assert.Throws<ArgumentException>(() => new WindowsCajaRecoverySecurityPolicy(directory,
                new SecurityIdentifier(WellKnownSidType.WorldSid, null)));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }
}
