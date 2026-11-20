using System.Text.Json;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class AnulacionJournalTests
{
    [Fact]
    public void Journal_is_independent_durable_and_contains_only_operation_identity()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"anulacion-journal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "anulaciones-journal.json");
        var installation = Guid.NewGuid();
        var operation = Guid.NewGuid();
        try
        {
            WriteInitial(path, installation);
            File.WriteAllText(path + ".lock", "");
            var store = new AnulacionJournal(path, installation, new TestPolicy());
            Assert.True(store.Begin(operation, 42));
            Assert.False(store.Begin(Guid.NewGuid(), 42));
            var bytes = File.ReadAllText(path);
            Assert.Contains(operation.ToString(), bytes);
            Assert.DoesNotContain("motivo", bytes, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", bytes, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("payload", bytes, StringComparison.OrdinalIgnoreCase);

            var afterRestart = new AnulacionJournal(path, installation, new TestPolicy());
            Assert.Equal(new AnulacionJournalState(installation, "Pendiente", operation, 42), afterRestart.Read());
            Assert.True(afterRestart.MarkTerminal(operation, 42));
            Assert.False(afterRestart.ClearAfterReconciliation(operation, 41));
            Assert.Equal("TerminalPorConciliar", afterRestart.Read()!.Estado);
            Assert.True(afterRestart.ClearAfterReconciliation(operation, 42));
            Assert.Equal("SinPendientes", afterRestart.Read()!.Estado);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void Missing_or_malformed_journal_fails_closed_without_repair()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"anulacion-journal-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "anulaciones-journal.json");
        try
        {
            var installation = Guid.NewGuid();
            var missing = new AnulacionJournal(path, installation, new TestPolicy());
            Assert.Null(missing.Read());
            Assert.False(missing.Begin(Guid.NewGuid(), 1));
            Assert.False(File.Exists(path));

            WriteInitial(path, installation);
            File.WriteAllText(path + ".lock", "");
            File.WriteAllText(path, JsonSerializer.Serialize(new { version = 99 }));
            Assert.Null(new AnulacionJournal(path, installation, new TestPolicy()).Read());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".lock")) File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    private static void WriteInitial(string path, Guid installation) =>
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            instalacion = installation,
            estado = "SinPendientes",
            operacion = (Guid?)null,
            propietario = (int?)null
        }));

    private sealed class TestPolicy : ICajaRecoverySecurityPolicy
    {
        public bool IsProtected(string path) => true;
    }
}
