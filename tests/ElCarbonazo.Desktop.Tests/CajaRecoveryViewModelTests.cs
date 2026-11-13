using System.Text.Json;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class CajaRecoveryViewModelTests
{
    [Fact]
    public async Task Recovery_requires_confirmation_and_applies_snapshot_before_clearing_journal()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"caja-view-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "journal.json");
        var installation = Guid.NewGuid();
        try
        {
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1, instalacion = installation, estado = "SinPendientes", operacion = (Guid?)null,
                propietario = (int?)null, tipo = (string?)null
            }));
            File.WriteAllText(path + ".lock", "");
            var store = new CajaRecoveryStore(path, installation, new Policy());
            var operation = Guid.NewGuid();
            Assert.True(store.Begin(operation, 1, "CerrarTurno"));
            var session = new FakeSession();
            var viewModel = new CajaRecoveryViewModel(store, session, () => 1);
            var transport = new FakeTransport(viewModel);
            viewModel.Attach(new CajaRecoveryCoordinator(store, transport, () => 1));
            Assert.True(viewModel.Open(() => { }));
            await viewModel.CancelarCommand.ExecuteAsync(null);
            Assert.Equal(0, transport.Cancellations);
            viewModel.ConfirmarCancelacion = true;
            await viewModel.CancelarCommand.ExecuteAsync(null);
            Assert.Equal(1, transport.Cancellations);
            Assert.Equal("TerminalPorConciliar", store.Read()!.Estado);
            await viewModel.ConciliarCommand.ExecuteAsync(null);
            Assert.Contains("Turno 17", viewModel.Fotografia, StringComparison.Ordinal);
            Assert.Equal("SinPendientes", store.Read()!.Estado);
            Assert.False(viewModel.ConfirmarCancelacion);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    private sealed class Policy : ICajaRecoverySecurityPolicy { public bool IsProtected(string path) => true; }

    private sealed class FakeTransport(ICajaRecoverySnapshotSink sink) : ICajaRecoveryTransport
    {
        public int Cancellations { get; private set; }
        public Task<CajaRecoveryOutcome> PrepareAsync(Guid operation, int actor, string type, object? arguments, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CajaRecoveryOutcome> ExecuteAsync(Guid operation, int actor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CajaRecoveryOutcome> QueryAsync(Guid operation, int actor, CancellationToken cancellationToken) => Task.FromResult(CajaRecoveryOutcome.Prepared);
        public Task<CajaRecoveryOutcome> CancelAsync(Guid operation, int actor, bool confirmaIntervencion, CancellationToken cancellationToken)
        {
            Cancellations++;
            return Task.FromResult(CajaRecoveryOutcome.Cancelled);
        }
        public Task<bool> ReconcileAsync(Guid operation, int actor, string type, CancellationToken cancellationToken) =>
            sink.ApplyShiftAsync(new CajaTurnoSnapshot(17, "Abierto", 10, 0, 0, 0, 10, 0), cancellationToken);
    }

    private sealed class FakeSession : ISessionManager
    {
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged { add { } remove { } }
        public bool HasSession => true;
        public SessionLifecycleState State => SessionLifecycleState.Active;
        public SessionSnapshot? CurrentSession => new(1, "jefa", "Jefa", DateTimeOffset.MaxValue, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue);
        public Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EvaluateState() { }
    }
}
