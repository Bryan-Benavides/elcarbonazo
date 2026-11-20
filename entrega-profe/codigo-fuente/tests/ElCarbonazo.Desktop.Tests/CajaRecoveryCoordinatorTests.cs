using System.Text.Json;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class CajaRecoveryCoordinatorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Session_change_during_write_keeps_original_journal_until_explicit_recovery(bool execute)
    {
        using var fixture = new Fixture();
        long generation = 1;
        var transport = new FakeTransport
        {
            OnExecute = () => generation++,
            OnCancel = () => generation++,
            ExecuteResult = CajaRecoveryOutcome.Completed
        };
        var coordinator = new CajaRecoveryCoordinator(fixture.Store, transport, () => 1, sessionGeneration: () => generation);
        var id = Guid.NewGuid();
        Assert.Equal(CajaRecoveryOutcome.Prepared, await coordinator.BeginAsync(id, 1, "CobrarCuenta"));
        var result = execute ? await coordinator.ExecuteAsync() : await coordinator.CancelAsync();
        Assert.Equal(CajaRecoveryOutcome.Uncertain, result);
        Assert.Equal(id, fixture.Store.Read()!.Operacion);
        Assert.Equal("Pendiente", fixture.Store.Read()!.Estado);
        Assert.False(await coordinator.ReconcileAsync());
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await coordinator.ExecuteAsync());
        Assert.Equal(execute ? 1 : 0, transport.ExecuteCalls);
        transport.QueryResult = execute ? CajaRecoveryOutcome.Completed : CajaRecoveryOutcome.Cancelled;
        Assert.Equal(transport.QueryResult, await coordinator.RecoverAsync());
        Assert.Equal("TerminalPorConciliar", fixture.Store.Read()!.Estado);
    }

    [Fact]
    public async Task Same_actor_new_session_cannot_execute_previous_preparation_or_clear_journal()
    {
        using var fixture = new Fixture();
        long generation = 1;
        var transport = new FakeTransport();
        var coordinator = new CajaRecoveryCoordinator(fixture.Store, transport, () => 1, sessionGeneration: () => generation);
        var id = Guid.NewGuid();
        Assert.Equal(CajaRecoveryOutcome.Prepared, await coordinator.BeginAsync(id, 1, "AbrirTurno"));
        generation++;
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await coordinator.ExecuteAsync());
        Assert.Equal(0, transport.ExecuteCalls);
        transport.QueryResult = CajaRecoveryOutcome.Completed;
        Assert.Equal(CajaRecoveryOutcome.Completed, await coordinator.RecoverAsync());
        transport.ReconcileResult = true;
        transport.OnReconcile = () => generation++;
        Assert.False(await coordinator.ReconcileAsync());
        Assert.Equal(id, fixture.Store.Read()!.Operacion);
        Assert.Equal("TerminalPorConciliar", fixture.Store.Read()!.Estado);
    }

    [Fact]
    public async Task Same_actor_new_session_during_prepare_keeps_journal_pending()
    {
        using var fixture = new Fixture();
        long generation = 1;
        var transport = new FakeTransport { OnPrepare = () => generation++, PrepareResult = CajaRecoveryOutcome.Completed };
        var coordinator = new CajaRecoveryCoordinator(fixture.Store, transport, () => 1, sessionGeneration: () => generation);
        var id = Guid.NewGuid();
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await coordinator.BeginAsync(id, 1, "AbrirTurno"));
        Assert.Equal("Pendiente", fixture.Store.Read()!.Estado);
        Assert.Equal(id, fixture.Store.Read()!.Operacion);
    }

    [Fact]
    public async Task Jefa_can_supervise_and_cancel_foreign_intention_only_with_confirmation()
    {
        using var fixture = new Fixture();
        var id = Guid.NewGuid();
        Assert.True(fixture.Store.Begin(id, 1, "CobrarCuenta"));
        var transport = new FakeTransport { QueryResult = CajaRecoveryOutcome.Prepared };
        var isJefa = false;
        var coordinator = new CajaRecoveryCoordinator(fixture.Store, transport, () => 2, () => isJefa);
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await coordinator.RecoverAsync());
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await coordinator.CancelAsync(confirmaIntervencion: true));
        Assert.Equal(0, transport.QueryCalls);
        Assert.Equal(0, transport.CancelCalls);
        isJefa = true;
        Assert.Equal(CajaRecoveryOutcome.Prepared, await coordinator.RecoverAsync());
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await coordinator.ExecuteAsync());
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await coordinator.CancelAsync());
        Assert.Equal(0, transport.CancelCalls);
        Assert.Equal(CajaRecoveryOutcome.Cancelled, await coordinator.CancelAsync(confirmaIntervencion: true));
        Assert.Equal(2, transport.LastActor);
        Assert.True(transport.SupervisionConfirmed);
        Assert.Equal(1, fixture.Store.Read()!.Propietario);
        Assert.Equal("TerminalPorConciliar", fixture.Store.Read()!.Estado);
    }

    [Fact]
    public async Task Execute_is_explicit_and_never_retried_after_uncertain_response()
    {
        using var fixture = new Fixture();
        var transport = new FakeTransport();
        var coordinator = fixture.Coordinator(transport, () => 1);
        var id = Guid.NewGuid();
        Assert.Equal(CajaRecoveryOutcome.Prepared, await coordinator.BeginAsync(id, 1, "AbrirTurno"));
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await coordinator.ExecuteAsync());
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await coordinator.ExecuteAsync());
        Assert.Equal(1, transport.ExecuteCalls);
        Assert.Equal(id, fixture.Store.Read()!.Operacion);
        transport.QueryResult = CajaRecoveryOutcome.NotFound;
        Assert.Equal(CajaRecoveryOutcome.NotFound, await coordinator.RecoverAsync());
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await coordinator.ExecuteAsync());
        Assert.Equal(1, transport.ExecuteCalls);
        transport.QueryResult = CajaRecoveryOutcome.Prepared;
        Assert.Equal(CajaRecoveryOutcome.Prepared, await coordinator.RecoverAsync());
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await coordinator.ExecuteAsync());
        Assert.Equal(1, transport.ExecuteCalls);
    }

    [Fact]
    public async Task Timeout_restart_query_and_reconciliation_never_repeat_prepare()
    {
        using var fixture = new Fixture();
        var id = Guid.NewGuid();
        var transport = new FakeTransport { PrepareError = true, QueryResult = CajaRecoveryOutcome.NotFound };
        var first = fixture.Coordinator(transport, () => 1);
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await first.BeginAsync(id, 1, "CobrarCuenta"));
        Assert.Equal(id, fixture.Store.Read()!.Operacion);
        var restarted = fixture.Coordinator(transport, () => 1);
        Assert.Equal(CajaRecoveryOutcome.NotFound, await restarted.RecoverAsync());
        Assert.False(await restarted.ReconcileAsync());
        transport.QueryResult = CajaRecoveryOutcome.Prepared;
        Assert.Equal(CajaRecoveryOutcome.Prepared, await restarted.RecoverAsync());
        Assert.False(await restarted.ReconcileAsync());
        Assert.Equal(id, fixture.Store.Read()!.Operacion);
        transport.QueryResult = CajaRecoveryOutcome.Completed;
        Assert.Equal(CajaRecoveryOutcome.Completed, await restarted.RecoverAsync());
        Assert.Equal("TerminalPorConciliar", fixture.Store.Read()!.Estado);
        transport.ReconcileResult = false;
        Assert.False(await restarted.ReconcileAsync());
        transport.ReconcileResult = true;
        Assert.True(await restarted.ReconcileAsync());
        Assert.Equal("SinPendientes", fixture.Store.Read()!.Estado);
        Assert.Equal(1, transport.PrepareCalls);
    }

    [Fact]
    public async Task Different_actor_and_storage_failure_block_new_operations()
    {
        using var fixture = new Fixture();
        var actor = 1;
        var transport = new FakeTransport();
        var coordinator = fixture.Coordinator(transport, () => actor);
        var id = Guid.NewGuid();
        Assert.Equal(CajaRecoveryOutcome.Prepared, await coordinator.BeginAsync(id, 1, "AbrirTurno"));
        actor = 2;
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await coordinator.RecoverAsync());
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await coordinator.BeginAsync(Guid.NewGuid(), 2, "CerrarTurno"));
        Assert.Equal(0, transport.QueryCalls);
        Assert.Equal(id, fixture.Store.Read()!.Operacion);
        File.Delete(fixture.Path + ".lock");
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await coordinator.BeginAsync(Guid.NewGuid(), 2, "AbrirTurno"));
        Assert.Equal(1, transport.PrepareCalls);
    }

    [Fact]
    public async Task Actor_change_during_response_keeps_the_original_intention_blocked()
    {
        using var fixture = new Fixture();
        var actor = 1;
        var transport = new FakeTransport { OnPrepare = () => actor = 2, PrepareResult = CajaRecoveryOutcome.Completed };
        var coordinator = fixture.Coordinator(transport, () => actor);
        var id = Guid.NewGuid();
        Assert.Equal(CajaRecoveryOutcome.Uncertain, await coordinator.BeginAsync(id, 1, "AbrirTurno"));
        Assert.Equal("Pendiente", fixture.Store.Read()!.Estado);
        Assert.Equal(id, fixture.Store.Read()!.Operacion);
        Assert.False(await coordinator.ReconcileAsync());
    }

    [Fact]
    public async Task Concurrent_begin_sends_only_one_intention()
    {
        using var fixture = new Fixture();
        var transport = new FakeTransport();
        var coordinator = fixture.Coordinator(transport, () => 1);
        var outcomes = await Task.WhenAll(coordinator.BeginAsync(Guid.NewGuid(), 1, "AbrirTurno"),
            coordinator.BeginAsync(Guid.NewGuid(), 1, "CerrarTurno"));
        Assert.Single(outcomes, x => x == CajaRecoveryOutcome.Prepared);
        Assert.Equal(1, transport.PrepareCalls);
        Assert.Equal("Pendiente", fixture.Store.Read()!.Estado);
    }

    private sealed class FakeTransport : ICajaRecoveryTransport
    {
        public bool PrepareError { get; init; }
        public Action? OnPrepare { get; init; }
        public Action? OnExecute { get; init; }
        public Action? OnCancel { get; init; }
        public Action? OnReconcile { get; set; }
        public CajaRecoveryOutcome PrepareResult { get; init; } = CajaRecoveryOutcome.Prepared;
        public CajaRecoveryOutcome QueryResult { get; set; }
        public CajaRecoveryOutcome ExecuteResult { get; init; } = CajaRecoveryOutcome.Uncertain;
        public bool ReconcileResult { get; set; }
        public int PrepareCalls { get; private set; }
        public int QueryCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public int LastActor { get; private set; }
        public bool SupervisionConfirmed { get; private set; }
        public Task<CajaRecoveryOutcome> PrepareAsync(Guid operation, int actor, string type, object? arguments, CancellationToken cancellationToken)
        {
            PrepareCalls++;
            OnPrepare?.Invoke();
            if (PrepareError) throw new HttpRequestException("Simulated disconnection");
            return Task.FromResult(PrepareResult);
        }
        public Task<CajaRecoveryOutcome> QueryAsync(Guid operation, int actor, CancellationToken cancellationToken)
        {
            QueryCalls++;
            return Task.FromResult(QueryResult);
        }
        public Task<CajaRecoveryOutcome> CancelAsync(Guid operation, int actor, bool confirmaIntervencion, CancellationToken cancellationToken)
        {
            CancelCalls++;
            OnCancel?.Invoke();
            LastActor = actor;
            SupervisionConfirmed = confirmaIntervencion;
            return Task.FromResult(CajaRecoveryOutcome.Cancelled);
        }
        public Task<CajaRecoveryOutcome> ExecuteAsync(Guid operation, int actor, CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            OnExecute?.Invoke();
            return Task.FromResult(ExecuteResult);
        }
        public Task<bool> ReconcileAsync(Guid operation, int actor, string type, CancellationToken cancellationToken)
        {
            OnReconcile?.Invoke();
            return Task.FromResult(ReconcileResult);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"caja-coordinator-{Guid.NewGuid():N}");
        private readonly Guid installation = Guid.NewGuid();
        public string Path { get; }
        public CajaRecoveryStore Store { get; }
        public Fixture()
        {
            Directory.CreateDirectory(directory);
            Path = System.IO.Path.Combine(directory, "journal.json");
            File.WriteAllBytes(Path, JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1,
                instalacion = installation,
                estado = "SinPendientes",
                operacion = (Guid?)null,
                propietario = (int?)null,
                tipo = (string?)null
            }));
            File.WriteAllText(Path + ".lock", "");
            Store = new CajaRecoveryStore(Path, installation, new TestPolicy());
        }
        public CajaRecoveryCoordinator Coordinator(ICajaRecoveryTransport transport, Func<int?> actor) => new(Store, transport, actor);
        public void Dispose()
        {
            File.Delete(Path);
            File.Delete(Path + ".lock");
            Directory.Delete(directory);
        }
    }
    private sealed class TestPolicy : ICajaRecoverySecurityPolicy { public bool IsProtected(string path) => true; }
}
