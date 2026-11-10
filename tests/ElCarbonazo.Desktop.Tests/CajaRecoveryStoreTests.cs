using System.Text.Json;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class CajaRecoveryStoreTests
{
    [Fact]
    public async Task Competing_process_models_cannot_replace_a_pending_operation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"caja-journal-race-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "journal.json");
        var installation = Guid.NewGuid();
        try
        {
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
            var one = Guid.NewGuid();
            var two = Guid.NewGuid();
            var first = new CajaRecoveryStore(path, installation, new TestPolicy(true));
            var second = new CajaRecoveryStore(path, installation, new TestPolicy(true));
            var outcomes = await Task.WhenAll(Task.Run(() => first.Begin(one, 1, "AbrirTurno")),
                Task.Run(() => second.Begin(two, 1, "AbrirTurno")));
            Assert.Single(outcomes, value => value);
            var persisted = first.Read();
            Assert.NotNull(persisted);
            Assert.Contains(persisted.Operacion, new Guid?[] { one, two });
            Assert.Equal("Pendiente", persisted.Estado);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void Journal_persists_identity_across_restarts_and_requires_terminal_reconciliation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"caja-journal-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "journal.json");
        var installation = Guid.NewGuid();
        var operation = Guid.NewGuid();
        try
        {
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
            var store = new CajaRecoveryStore(path, installation, new TestPolicy(true));
            Assert.True(store.Begin(operation, 7, "CobrarCuenta"));
            Assert.False(store.Begin(Guid.NewGuid(), 7, "AbrirTurno"));
            var restarted = new CajaRecoveryStore(path, installation, new TestPolicy(true));
            Assert.Equal(operation, restarted.Read()!.Operacion);
            Assert.False(restarted.CompleteReconciliation(operation, 7));
            Assert.False(restarted.MarkTerminal(operation, 8));
            Assert.True(restarted.MarkTerminal(operation, 7));
            Assert.Equal("TerminalPorConciliar", restarted.Read()!.Estado);
            Assert.True(restarted.CompleteReconciliation(operation, 7));
            Assert.Equal("SinPendientes", restarted.Read()!.Estado);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void Missing_or_unverified_files_do_not_create_or_unlock_journal()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-caja-{Guid.NewGuid():N}.json");
        var store = new CajaRecoveryStore(path, Guid.NewGuid(), new TestPolicy(false));
        Assert.Null(store.Read());
        Assert.False(store.Begin(Guid.NewGuid(), 1, "AbrirTurno"));
        Assert.False(File.Exists(path));
    }

    private sealed class TestPolicy(bool allowed) : ICajaRecoverySecurityPolicy
    {
        public bool IsProtected(string path) => allowed;
    }
}
