using System.Net;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class CocinaViewModelTests
{
    private static Task Dispatch(Action action) { action(); return Task.CompletedTask; }
    private static CocinaComandaDto Ticket(int id = 1) => new(id, 1, 4, 4, null, 1, DateTimeOffset.UnixEpoch,
        "Abierta", 7, [new CocinaLineaDto(id, "DEMO", 2, "Sin sal")]);

    [Fact]
    public async Task Successful_transitions_update_and_remove_the_ticket()
    {
        var api = new FakeApi();
        using var vm = new CocinaViewModel(new FakeSession(), api, () => { }, Dispatch);
        var ticket = Assert.Single(vm.Comandas);
        await vm.AvanzarCommand.ExecuteAsync(ticket);
        Assert.Equal("En Preparación", Assert.Single(vm.Comandas).Estado);
        await vm.AvanzarCommand.ExecuteAsync(Assert.Single(vm.Comandas));
        Assert.Empty(vm.Comandas);
        Assert.Contains("no significa", vm.Mensaje, StringComparison.Ordinal);
        Assert.Equal(2, api.Writes);
    }

    [Fact]
    public async Task Uncertain_write_remains_blocked_after_successful_read_and_cannot_return()
    {
        var api = new FakeApi { Failure = new IOException("lost response") };
        var returned = false;
        using var vm = new CocinaViewModel(new FakeSession(), api, () => returned = true, Dispatch);
        await vm.AvanzarCommand.ExecuteAsync(Assert.Single(vm.Comandas));
        Assert.True(vm.ResultadoIncierto);
        Assert.False(vm.PuedeVolver);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!vm.Conectado) await Task.Delay(25, timeout.Token);
        Assert.False(vm.PuedeOperar);
        vm.VolverCommand.Execute(null);
        Assert.False(returned);
        await vm.AvanzarCommand.ExecuteAsync(Assert.Single(vm.Comandas));
        Assert.Equal(1, api.Writes);
        Assert.Contains("no demuestra", vm.Mensaje, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conflict_requires_a_new_human_decision_after_refresh()
    {
        var api = new FakeApi { Failure = new ApiException(HttpStatusCode.Conflict, null) };
        using var vm = new CocinaViewModel(new FakeSession(), api, () => { }, Dispatch);
        await vm.AvanzarCommand.ExecuteAsync(Assert.Single(vm.Comandas));
        Assert.False(vm.Conectado);
        Assert.False(vm.ResultadoIncierto);
        Assert.Equal(1, api.Writes);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!vm.Conectado) await Task.Delay(25, timeout.Token);
        Assert.Equal(1, api.Writes);
        Assert.True(vm.PuedeOperar);
    }

    [Fact]
    public void Empty_queue_is_not_confused_with_a_failed_read()
    {
        using var empty = new CocinaViewModel(new FakeSession(), new FakeApi { Empty = true }, () => { }, Dispatch);
        Assert.True(empty.ColaVacia);
        using var failed = new CocinaViewModel(new FakeSession(), new FakeApi { ReadFailure = true }, () => { }, Dispatch);
        Assert.False(failed.ColaVacia);
        Assert.False(failed.PuedeOperar);
    }

    [Fact]
    public async Task A_write_waits_for_read_and_does_not_send_a_stale_card()
    {
        var api = new BlockingReadApi();
        using var vm = new CocinaViewModel(new FakeSession(), api, () => { }, Dispatch);
        var old = Assert.Single(vm.Comandas);
        await api.Started.Task.WaitAsync(TimeSpan.FromSeconds(8));
        var pending = vm.AvanzarCommand.ExecuteAsync(old);
        Assert.Equal(0, api.Writes);
        api.Release.TrySetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, api.Writes);
        Assert.Contains("cambió", vm.Mensaje, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Paused_session_blocks_mutation_and_recovers_only_after_read()
    {
        var api = new FakeApi();
        var session = new FakeSession();
        using var vm = new CocinaViewModel(session, api, () => { }, Dispatch);
        using var initialTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!vm.Conectado) await Task.Delay(25, initialTimeout.Token);
        var ticket = Assert.Single(vm.Comandas);
        session.ChangeState(SessionLifecycleState.Paused);
        Assert.False(vm.PuedeOperar);
        var readsWhilePaused = api.Reads;
        await Task.Delay(2200);
        Assert.Equal(readsWhilePaused, api.Reads);
        await vm.AvanzarCommand.ExecuteAsync(ticket);
        Assert.Equal(0, api.Writes);
        session.ChangeState(SessionLifecycleState.Active);
        Assert.False(vm.PuedeOperar);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!vm.PuedeOperar) await Task.Delay(25, timeout.Token);
    }

    private sealed class BlockingReadApi : ICocinaApiService
    {
        private int _reads;
        public int Writes { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CocinaPaginaDto> ConsultarAsync(string? cursor = null, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _reads) == 2)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
                return new CocinaPaginaDto([Ticket() with { Estado = "En Preparación", Version = 8 }], null);
            }
            return new CocinaPaginaDto([Ticket()], null);
        }
        public Task<CocinaComandaDto> CambiarEstadoAsync(CocinaComandaDto comanda, CancellationToken cancellationToken = default)
        {
            Writes++;
            return Task.FromResult(comanda);
        }
    }

    private sealed class FakeApi : ICocinaApiService
    {
        public Exception? Failure { get; init; }
        public bool Empty { get; init; }
        public bool ReadFailure { get; init; }
        public int Writes { get; private set; }
        public int Reads { get; private set; }
        public Task<CocinaPaginaDto> ConsultarAsync(string? cursor = null, CancellationToken cancellationToken = default)
        {
            Reads++;
            if (ReadFailure) throw new HttpRequestException();
            return Task.FromResult(new CocinaPaginaDto(Empty ? [] : [Ticket()], null));
        }
        public Task<CocinaComandaDto> CambiarEstadoAsync(CocinaComandaDto comanda, CancellationToken cancellationToken = default)
        {
            Writes++;
            if (Failure is not null) throw Failure;
            return Task.FromResult(comanda with { Estado = comanda.Estado == "Abierta" ? "En Preparación" : "Lista" });
        }
    }

    private sealed class FakeSession : ISessionManager
    {
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged;
        public bool HasSession => true;
        public SessionLifecycleState State { get; private set; } = SessionLifecycleState.Active;
        public void ChangeState(SessionLifecycleState state)
        {
            var previous = State;
            State = state;
            SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs(previous, state));
        }
        public SessionSnapshot? CurrentSession => null;
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
