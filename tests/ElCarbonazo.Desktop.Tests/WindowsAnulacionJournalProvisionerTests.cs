using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class WindowsAnulacionJournalProvisionerTests
{
    [Fact]
    public void Fresh_provisioning_is_explicit_and_never_overwrites_existing_journal()
    {
        if (!OperatingSystem.IsWindows()) return;
        VerifyProvisioning();
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyProvisioning()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode");
        var parent = Path.Combine(root, $"anulacion-provision-{Guid.NewGuid():N}");
        var directory = Path.Combine(parent, "anulaciones");
        var path = Path.Combine(directory, "anulaciones-journal.json");
        var installation = Guid.NewGuid();
        Directory.CreateDirectory(parent);
        try
        {
            Assert.False(WindowsAnulacionJournalProvisioner.CreateFresh(directory, Guid.Empty));
            Assert.False(Directory.Exists(directory));

            Assert.True(WindowsAnulacionJournalProvisioner.CreateFresh(directory, installation));
            var operatorSid = WindowsIdentity.GetCurrent().User!;
            var policy = new WindowsCajaRecoverySecurityPolicy(directory, operatorSid);
            Assert.True(policy.IsProtected(path));
            Assert.True(policy.IsProtected(path + ".lock"));
            var journal = new AnulacionJournal(path, installation, policy);
            Assert.Equal("SinPendientes", journal.Read()!.Estado);
            Assert.Null(new AnulacionJournal(path, Guid.NewGuid(), policy).Read());

            using (var json = JsonDocument.Parse(File.ReadAllBytes(path)))
                Assert.Equal(new[] { "estado", "instalacion", "operacion", "propietario", "version" },
                    json.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToArray());

            var operation = Guid.NewGuid();
            Assert.True(journal.Begin(operation, 7));
            Assert.False(WindowsAnulacionJournalProvisioner.CreateFresh(directory, installation));
            Assert.Equal(operation, journal.Read()!.Operacion);
            Assert.Equal("Pendiente", journal.Read()!.Estado);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
            if (Directory.Exists(directory)) Directory.Delete(directory);
            if (Directory.Exists(parent)) Directory.Delete(parent);
        }
    }

    [Fact]
    public void Provisioning_requires_an_existing_parent_and_never_creates_it()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "opencode");
        var missingParent = Path.Combine(root, $"anulacion-missing-{Guid.NewGuid():N}");
        var directory = Path.Combine(missingParent, "anulaciones");
        Assert.False(WindowsAnulacionJournalProvisioner.CreateFresh(directory, Guid.NewGuid()));
        Assert.False(Directory.Exists(missingParent));
    }
}
