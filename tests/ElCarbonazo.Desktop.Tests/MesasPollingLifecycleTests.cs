using System.Threading.Channels;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class MesasPollingLifecycleTests
{
    private static Task DispatchInline(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SlowReadNeverOverlapsAnotherRead()
    {
        var session = new FakeSession();
        var service = new ControlledMesas();
        using var viewModel = new MesasViewModel("Ana", "Mesero", session, service, DispatchInline);
        var first = await service.NextAsync();

        await Task.Delay(TimeSpan.FromMilliseconds(2200));
        Assert.Equal(1, service.Calls);
        Assert.Equal(1, service.MaxConcurrent);

        first.SetResult([new MesaDto(1, null, 1, 4, "Libre", 1)]);
        await service.NextAsync();
        Assert.Equal(1, service.MaxConcurrent);
    }

    [Fact]
    public async Task PauseStopsReadsAndResumesOnlyWhenSessionBecomesActive()
    {
        var session = new FakeSession();
        var service = new ControlledMesas();
        using var viewModel = new MesasViewModel("Ana", "Mesero", session, service, DispatchInline);
        var first = await service.NextAsync();
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MesasViewModel.EstadoConexion) && viewModel.EstadoConexion.StartsWith("Sesión en pausa"))
                paused.TrySetResult();
        };

        session.Change(SessionLifecycleState.Paused);
        first.SetResult([new MesaDto(1, null, 1, 4, "Libre", 1)]);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(viewModel.Mesas);
        Assert.Equal(1, service.Calls);
        Assert.Equal(0, session.HumanInteractions);

        session.Change(SessionLifecycleState.Active);
        await service.NextAsync();
        Assert.Equal(2, service.Calls);
        Assert.Equal(0, session.HumanInteractions);
    }

    [Fact]
    public async Task DisposingCancelsInflightReadAndStopsPolling()
    {
        var session = new FakeSession();
        var service = new ControlledMesas();
        var viewModel = new MesasViewModel("Ana", "Mesero", session, service, DispatchInline);
        await service.NextAsync();

        viewModel.Dispose();
        await service.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Change(SessionLifecycleState.Active);
        Assert.Equal(1, service.Calls);
    }

    [Theory]
    [InlineData(SessionLifecycleState.SignedOut)]
    [InlineData(SessionLifecycleState.Expired)]
    [InlineData(SessionLifecycleState.SecurityBlocked)]
    [InlineData(SessionLifecycleState.ShuttingDown)]
    public async Task TerminalSessionStateCancelsInflightRead(SessionLifecycleState state)
    {
        var session = new FakeSession();
        var service = new ControlledMesas();
        using var viewModel = new MesasViewModel("Ana", "Mesero", session, service, DispatchInline);
        await service.NextAsync();

        session.Change(state);
        await service.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task DetailDisplaysGroupMembersFromLoadedSnapshot()
    {
        var session = new FakeSession();
        var service = new ControlledMesas();
        using var viewModel = new MesasViewModel("Ana", "Mesero", session, service, DispatchInline);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MesasViewModel.EstadoConexion) && viewModel.EstadoConexion == "Conectado")
                loaded.TrySetResult();
        };
        var first = await service.NextAsync();
        first.SetResult([
            new MesaDto(1, 4, 1, 4, "Agrupada", 1),
            new MesaDto(2, 4, 2, 6, "Agrupada", 2)
        ]);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.SeleccionarMesaCommand.Execute(viewModel.Mesas[0]);
        Assert.True(viewModel.HayMesaSeleccionada);
        Assert.Equal("Mesa 01", viewModel.DetalleTitulo);
        Assert.Equal("Grupo 4: mesas 1, 2", viewModel.DetalleGrupo);
        viewModel.SeleccionarMesaCommand.Execute(viewModel.Mesas[1]);
        Assert.Equal("Mesa 02", viewModel.DetalleTitulo);
        viewModel.SeleccionarMesaCommand.Execute(viewModel.Mesas[1]);
        Assert.False(viewModel.HayMesaSeleccionada);
        viewModel.SeleccionarMesaCommand.Execute(viewModel.Mesas[0]);
        viewModel.CerrarDetalleCommand.Execute(null);
        Assert.False(viewModel.HayMesaSeleccionada);
    }

    [Theory]
    [InlineData("Mesero", "Libre", true, true, false)]
    [InlineData("Supervisor", "Reservada", false, true, true)]
    [InlineData("Jefa", "Ocupada", false, false, true)]
    [InlineData("Personal de Cocina", "Libre", false, false, false)]
    [InlineData("Mesero", "Agrupada", false, false, false)]
    public async Task ActionsRespectRoleAndTransition(string role, string estado, bool reservar, bool ocupar, bool liberar)
    {
        var service = new ControlledMesas();
        using var vm = new MesasViewModel("Ana", role, new FakeSession(), service, DispatchInline);
        var first = await service.NextAsync();
        first.SetResult([new MesaDto(1, null, 1, 4, estado, 7)]);
        await WaitUntilAsync(() => vm.Mesas.Count == 1);
        vm.SeleccionarMesaCommand.Execute(vm.Mesas[0]);
        Assert.Equal(reservar, vm.PuedeReservar);
        Assert.Equal(ocupar, vm.PuedeOcupar);
        Assert.Equal(liberar, vm.PuedeLiberar);
    }

    [Fact]
    public async Task ReleaseRequiresConfirmationAndUsesCurrentVersion()
    {
        var service = new ControlledMesas();
        using var vm = new MesasViewModel("Ana", "Mesero", new FakeSession(), service, DispatchInline);
        (await service.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Ocupada", 7)]);
        await WaitUntilAsync(() => vm.Mesas.Count == 1);
        vm.SeleccionarMesaCommand.Execute(vm.Mesas[0]);
        await vm.LiberarCommand.ExecuteAsync(null);
        Assert.Equal(0, service.UpdateCalls);
        vm.SolicitarLiberacionCommand.Execute(null);
        Assert.True(vm.ConfirmarLiberacion);
        vm.CancelarLiberacionCommand.Execute(null);
        Assert.False(vm.ConfirmarLiberacion);
        Assert.Equal(0, service.UpdateCalls);
        Assert.Equal("Ocupada", vm.MesaSeleccionada!.Estado);
        vm.SolicitarLiberacionCommand.Execute(null);
        await vm.LiberarCommand.ExecuteAsync(null);
        Assert.Equal((1, "Libre", 7u), service.LastUpdate);
        Assert.Equal("Libre", vm.MesaSeleccionada!.Estado);
    }

    [Fact]
    public async Task UncertainWriteRequiresSuccessfulReadAndHumanAcknowledgement()
    {
        var service = new ControlledMesas { UpdateFailure = new HttpRequestException() };
        using var vm = new MesasViewModel("Ana", "Mesero", new FakeSession(), service, DispatchInline);
        (await service.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Libre", 1)]);
        await WaitUntilAsync(() => vm.Mesas.Count == 1);
        vm.SeleccionarMesaCommand.Execute(vm.Mesas[0]);
        await vm.OcuparCommand.ExecuteAsync(null);
        Assert.True(vm.ResultadoIncierto);
        Assert.False(vm.PuedeOcupar);
        vm.ReconocerIncertidumbreCommand.Execute(null);
        Assert.True(vm.ResultadoIncierto);
        (await service.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Libre", 1)]);
        await WaitUntilAsync(() => vm.PuedeReconocer);
        Assert.Contains("no demuestra", vm.MensajeAccion);
        vm.ReconocerIncertidumbreCommand.Execute(null);
        Assert.True(vm.PuedeOcupar);
        Assert.Equal(1, service.UpdateCalls);
    }

    [Fact]
    public async Task UncertaintyCannotBeAcknowledgedAfterConnectionFailsUntilNewRead()
    {
        var service = new ControlledMesas { UpdateFailure = new HttpRequestException() };
        using var vm = new MesasViewModel("Ana", "Mesero", new FakeSession(), service, DispatchInline);
        (await service.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Libre", 1)]);
        await WaitUntilAsync(() => vm.Mesas.Count == 1);
        vm.SeleccionarMesaCommand.Execute(vm.Mesas[0]);
        await vm.OcuparCommand.ExecuteAsync(null);
        (await service.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Libre", 1)]);
        await WaitUntilAsync(() => vm.PuedeReconocer);
        (await service.NextAsync()).SetException(new HttpRequestException());
        await WaitUntilAsync(() => vm.EstadoConexion.StartsWith("Sin conexión"));
        Assert.False(vm.PuedeReconocer);
        vm.ReconocerIncertidumbreCommand.Execute(null);
        Assert.True(vm.ResultadoIncierto);
        (await service.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Libre", 1)]);
        await WaitUntilAsync(() => vm.PuedeReconocer);
    }

    [Fact]
    public async Task ConflictReconcilesWithoutRetryingWrite()
    {
        var service = new ControlledMesas
        {
            UpdateFailure = new ApiException(System.Net.HttpStatusCode.Conflict,
                new ApiProblem(409, "MESA_CONFLICTO_CONCURRENCIA", null, null, null))
        };
        using var vm = new MesasViewModel("Ana", "Mesero", new FakeSession(), service, DispatchInline);
        (await service.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Libre", 1)]);
        await WaitUntilAsync(() => vm.Mesas.Count == 1);
        vm.SeleccionarMesaCommand.Execute(vm.Mesas[0]);
        await vm.OcuparCommand.ExecuteAsync(null);
        Assert.False(vm.PuedeOcupar);
        Assert.Contains("otra terminal", vm.MensajeAccion);
        (await service.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Reservada", 2)]);
        await WaitUntilAsync(() => vm.MesaSeleccionada?.Estado == "Reservada");
        Assert.Equal(1, service.UpdateCalls);
    }

    [Theory]
    [InlineData(401, "autenticación")]
    [InlineData(403, "rol")]
    public async Task AuthenticationAndPermissionRejectionsAreNotReportedAsOffline(int status, string expected)
    {
        var service = new ControlledMesas { UpdateFailure = new ApiException((System.Net.HttpStatusCode)status, null) };
        using var vm = new MesasViewModel("Ana", "Mesero", new FakeSession(), service, DispatchInline);
        (await service.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Libre", 1)]);
        await WaitUntilAsync(() => vm.Mesas.Count == 1);
        vm.SeleccionarMesaCommand.Execute(vm.Mesas[0]);
        await vm.OcuparCommand.ExecuteAsync(null);
        Assert.Contains(expected, vm.MensajeAccion);
        Assert.False(vm.ResultadoIncierto);
        Assert.Equal("Conectado", vm.EstadoConexion);
        Assert.Equal(1, service.UpdateCalls);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    [Fact]
    public async Task MutationWaitsForActivePollingWithoutApplyingItsStaleResult()
    {
        var service = new ControlledMesas();
        using var vm = new MesasViewModel("Ana", "Mesero", new FakeSession(), service, DispatchInline);
        (await service.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Libre", 1)]);
        await WaitUntilAsync(() => vm.Mesas.Count == 1);
        vm.SeleccionarMesaCommand.Execute(vm.Mesas[0]);
        var pendingRead = await service.NextAsync();
        var mutation = vm.OcuparCommand.ExecuteAsync(null);
        Assert.Equal(0, service.UpdateCalls);
        pendingRead.SetResult([new MesaDto(1, null, 1, 4, "Reservada", 2)]);
        await mutation;
        Assert.Equal((1, "Ocupada", 1u), service.LastUpdate);
        Assert.Equal("Ocupada", vm.MesaSeleccionada!.Estado);
    }

    [Fact]
    public async Task ReadDeadlineCoversJsonBodyProcessing()
    {
        var session = new FakeSession { PollResponse = () => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(new WaitingStream()) } };
        var service = new MesasApiService(session);
        var started = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync());
        Assert.InRange(started.Elapsed.TotalSeconds, 4, 12);
    }

    private sealed class WaitingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task CreateGroupUsesLatestVersionsAndAppliesResponse()
    {
        var service = new ControlledMesas();
        var groups = new FakeGroups();
        using var vm = new MesasViewModel("Ana", "Mesero", new FakeSession(), service, DispatchInline, groups);
        (await service.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Libre", 1), new MesaDto(2, null, 2, 4, "Libre", 2)]);
        await WaitUntilAsync(() => vm.Mesas.Count == 2);
        vm.IniciarGrupoCommand.Execute(null);
        vm.SeleccionarMesaCommand.Execute(vm.Mesas[0]);
        vm.SeleccionarMesaCommand.Execute(vm.Mesas[1]);
        Assert.True(vm.PuedeConfirmarGrupo);
        (await service.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Libre", 7), new MesaDto(2, null, 2, 4, "Libre", 8)]);
        await WaitUntilAsync(() => vm.Mesas.FirstOrDefault()?.Version == 7 && vm.Mesas.Count == 2);
        await vm.CrearGrupoCommand.ExecuteAsync(null);
        Assert.Equal(new uint[] { 7, 8 }, groups.Created!.Select(m => m.Version));
        Assert.All(vm.Mesas, m => Assert.Equal("Agrupada", m.Estado));
        Assert.False(vm.SeleccionandoGrupo);
    }

    [Fact]
    public async Task PollRemovesIneligibleGroupSelectionAndKitchenCannotStart()
    {
        var service = new ControlledMesas();
        using var vm = new MesasViewModel("Ana", "Mesero", new FakeSession(), service, DispatchInline);
        (await service.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Libre", 1), new MesaDto(2, null, 2, 4, "Libre", 2)]);
        await WaitUntilAsync(() => vm.Mesas.Count == 2);
        vm.IniciarGrupoCommand.Execute(null);
        vm.SeleccionarMesaCommand.Execute(vm.Mesas[0]);
        vm.SeleccionarMesaCommand.Execute(vm.Mesas[1]);
        (await service.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Ocupada", 3), new MesaDto(2, null, 2, 4, "Libre", 2)]);
        await WaitUntilAsync(() => vm.MesasSeleccionadasGrupo.Length == 1);
        Assert.False(vm.PuedeConfirmarGrupo);
        Assert.Contains("retiraron", vm.MensajeAccion);
        var kitchenService = new ControlledMesas();
        using var kitchen = new MesasViewModel("Ana", "Personal de Cocina", new FakeSession(), kitchenService, DispatchInline);
        (await kitchenService.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Libre", 1)]);
        await WaitUntilAsync(() => kitchen.Mesas.Count == 1);
        kitchen.IniciarGrupoCommand.Execute(null);
        Assert.False(kitchen.SeleccionandoGrupo);
    }

    [Fact]
    public async Task CloseGroupRequiresConfirmationAndRefreshBeforeFurtherWrites()
    {
        var service = new ControlledMesas();
        var groups = new FakeGroups();
        using var vm = new MesasViewModel("Ana", "Jefa", new FakeSession(), service, DispatchInline, groups);
        (await service.NextAsync()).SetResult([new MesaDto(1, 9, 1, 4, "Agrupada", 1)]);
        await WaitUntilAsync(() => vm.Mesas.Count == 1);
        vm.SeleccionarMesaCommand.Execute(vm.Mesas[0]);
        await vm.CerrarGrupoCommand.ExecuteAsync(null);
        Assert.Null(groups.Closed);
        vm.SolicitarCierreGrupoCommand.Execute(null);
        await vm.CerrarGrupoCommand.ExecuteAsync(null);
        Assert.Equal(9, groups.Closed);
        Assert.False(vm.PuedeIniciarGrupo);
        (await service.NextAsync()).SetResult([new MesaDto(1, null, 1, 4, "Libre", 2)]);
        await WaitUntilAsync(() => vm.PuedeIniciarGrupo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GroupFailureIsNotRetriedAndRequiresReconciliation(bool uncertain)
    {
        var service = new ControlledMesas();
        var groups = new FakeGroups
        {
            Failure = uncertain ? new HttpRequestException() : new ApiException(System.Net.HttpStatusCode.Conflict,
                new ApiProblem(409, "GRUPO_CON_PEDIDOS_PENDIENTES", null, null, null))
        };
        using var vm = new MesasViewModel("Ana", "Jefa", new FakeSession(), service, DispatchInline, groups);
        (await service.NextAsync()).SetResult([new MesaDto(1, 9, 1, 4, "Agrupada", 1)]);
        await WaitUntilAsync(() => vm.Mesas.Count == 1);
        vm.SeleccionarMesaCommand.Execute(vm.Mesas[0]);
        vm.SolicitarCierreGrupoCommand.Execute(null);
        await vm.CerrarGrupoCommand.ExecuteAsync(null);
        Assert.False(vm.PuedeCerrarGrupo);
        Assert.Equal(uncertain, vm.ResultadoIncierto);
        Assert.Contains(uncertain ? "no demuestra" : "cuenta pendiente", vm.MensajeAccion);
        (await service.NextAsync()).SetResult([new MesaDto(1, 9, 1, 4, "Agrupada", 1)]);
        await WaitUntilAsync(() => uncertain ? vm.PuedeReconocer : vm.PuedeCerrarGrupo);
        if (uncertain)
        {
            Assert.False(vm.PuedeCerrarGrupo);
            vm.ReconocerIncertidumbreCommand.Execute(null);
            Assert.True(vm.PuedeCerrarGrupo);
        }
        Assert.Equal(1, groups.CloseCalls);
    }

    private sealed class FakeGroups : IGruposMesaApiService
    {
        public IReadOnlyList<MesaVersionRequest>? Created { get; private set; }
        public int? Closed { get; private set; }
        public int CloseCalls { get; private set; }
        public Exception? Failure { get; init; }
        public Task<GrupoMesaDto> CreateAsync(IReadOnlyList<MesaVersionRequest> mesas, CancellationToken cancellationToken = default)
        {
            Created = mesas;
            var result = mesas.Select(m => new MesaDto(m.IdMesa, 9, m.IdMesa, 4, "Agrupada", m.Version + 1)).ToArray();
            return Task.FromResult(new GrupoMesaDto(9, 1, DateTimeOffset.UtcNow, true, result));
        }
        public Task CloseAsync(int id, CancellationToken cancellationToken = default)
        {
            CloseCalls++;
            Closed = id;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class ControlledMesas : IMesasApiService
    {
        private readonly Channel<TaskCompletionSource<IReadOnlyList<MesaDto>>> _started = Channel.CreateUnbounded<TaskCompletionSource<IReadOnlyList<MesaDto>>>();
        private int _calls;
        private int _active;
        private int _maxConcurrent;
        public int Calls => Volatile.Read(ref _calls);
        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int UpdateCalls { get; private set; }
        public (int, string, uint) LastUpdate { get; private set; }
        public Exception? UpdateFailure { get; init; }

        public async Task<IReadOnlyList<MesaDto>> GetAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            var active = Interlocked.Increment(ref _active);
            if (active > _maxConcurrent) _maxConcurrent = active;
            var response = new TaskCompletionSource<IReadOnlyList<MesaDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _started.Writer.WriteAsync(response, cancellationToken);
            try { return await response.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
            finally { Interlocked.Decrement(ref _active); }
        }

        public async Task<TaskCompletionSource<IReadOnlyList<MesaDto>>> NextAsync() =>
            await _started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(6));

        public Task<MesaDto> UpdateEstadoAsync(int id, string estado, uint version, CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            LastUpdate = (id, estado, version);
            return UpdateFailure is null
                ? Task.FromResult(new MesaDto(id, null, id, 4, estado, version + 1))
                : Task.FromException<MesaDto>(UpdateFailure);
        }
    }

    private sealed class FakeSession : ISessionManager
    {
        public Func<HttpResponseMessage>? PollResponse { get; init; }
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged;
        public SessionLifecycleState State { get; private set; } = SessionLifecycleState.Active;
        public bool HasSession => State is SessionLifecycleState.Active or SessionLifecycleState.Expiring or SessionLifecycleState.Paused;
        public SessionSnapshot? CurrentSession => null;
        public int HumanInteractions { get; private set; }
        public void Change(SessionLifecycleState state)
        {
            var previous = State;
            State = state;
            SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs(previous, state));
        }
        public Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default) { HumanInteractions++; return Task.CompletedTask; }
        public Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default) =>
            PollResponse is null ? throw new NotSupportedException() : Task.FromResult(PollResponse());
        public Task ContinueSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EvaluateState() { }
    }
}
