using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class PedidosSalonViewModelTests
{
    [Fact]
    public void Expired_session_never_enables_salon_writes()
    {
        using var vm = new PedidosSalonViewModel(1, null, "Mesa 1", new ExpiredCatalogFake(), new AccountFake(),
            () => { }, new SuccessSendFake(), 7);
        Assert.False(vm.Conectado);
        Assert.False(vm.PuedeEnviar);
        Assert.Contains("Sesión finalizada", vm.EstadoLectura, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejected_manual_replay_does_not_release_uncertainty()
    {
        var sender = new UncertainThenRejectedFake();
        using var vm = new PedidosSalonViewModel(1, null, "Mesa 1", new CatalogFake(), new AccountFake(),
            () => { }, sender, 7);
        vm.AgregarCommand.Execute(Assert.Single(vm.Productos));
        vm.SolicitarEnvioCommand.Execute(null);
        await vm.ConfirmarEnvioCommand.ExecuteAsync(null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!vm.PuedeRepetir) await Task.Delay(25, timeout.Token);
        await vm.RepetirEnvioCommand.ExecuteAsync(null);
        Assert.Equal(2, sender.Count);
        Assert.True(vm.ResultadoIncierto);
        Assert.False(vm.PuedeVolver);
        Assert.False(vm.PuedeEnviar);
        Assert.Single(vm.Borrador);
    }

    [Fact]
    public async Task Unexpected_response_io_failure_is_uncertain_and_cannot_create_new_intention()
    {
        var sender = new IoFailureSendFake();
        using var vm = new PedidosSalonViewModel(1, null, "Mesa 1", new CatalogFake(), new AccountFake(),
            () => { }, sender, 7);
        vm.AgregarCommand.Execute(Assert.Single(vm.Productos));
        vm.SolicitarEnvioCommand.Execute(null);
        await vm.ConfirmarEnvioCommand.ExecuteAsync(null);
        Assert.True(vm.ResultadoIncierto);
        Assert.False(vm.PuedeEnviar);
        Assert.False(vm.PuedeVolver);
        Assert.Single(vm.Borrador);
        Assert.Equal(1, sender.Count);
    }

    [Fact]
    public async Task Catalog_conflict_does_not_retry_or_clear_draft()
    {
        var sender = new ConflictSendFake();
        using var vm = new PedidosSalonViewModel(1, null, "Mesa 1", new CatalogFake(), new AccountFake(),
            () => { }, sender, 7);
        vm.AgregarCommand.Execute(Assert.Single(vm.Productos));
        vm.SolicitarEnvioCommand.Execute(null);
        await vm.ConfirmarEnvioCommand.ExecuteAsync(null);
        Assert.Equal(1, sender.Count);
        Assert.False(vm.ResultadoIncierto);
        Assert.False(vm.PuedeEnviar);
        Assert.Single(vm.Borrador);
        Assert.Contains("catálogo cambió", vm.Mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Paused_session_never_enables_write_or_claims_an_empty_account()
    {
        using var vm = new PedidosSalonViewModel(1, null, "Mesa 1", new PausedCatalogFake(), new AccountFake(),
            () => { }, new SuccessSendFake(), 7);
        Assert.False(vm.Conectado);
        Assert.False(vm.PuedeEnviar);
        Assert.Equal("Cuenta aún no consultada", vm.EstadoCuenta);
        Assert.Equal("—", vm.TotalCuenta);
        Assert.Contains("pausa", vm.EstadoLectura, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Disconnect_preserves_last_data_but_disables_edit_and_send()
    {
        var catalog = new DisconnectCatalogFake();
        var sending = new SuccessSendFake();
        using var vm = new PedidosSalonViewModel(1, null, "Mesa 1", catalog, new AccountFake(), () => { }, sending, 7);
        vm.AgregarCommand.Execute(Assert.Single(vm.Productos));
        await catalog.Disconnected.Task.WaitAsync(TimeSpan.FromSeconds(8));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (vm.Conectado) await Task.Delay(25, timeout.Token);
        Assert.Single(vm.Productos);
        Assert.Single(vm.Borrador);
        Assert.False(vm.PuedeEditar);
        Assert.False(vm.PuedeEnviar);
        Assert.Equal(0, sending.Count);
        Assert.Contains("última consulta", vm.EstadoLectura, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_waits_for_inflight_account_read_before_starting_write()
    {
        var accounts = new BlockingAccountFake();
        var sending = new SuccessSendFake();
        using var vm = new PedidosSalonViewModel(1, null, "Mesa 1", new CatalogFake(), accounts,
            () => { }, sending, 7);
        vm.AgregarCommand.Execute(Assert.Single(vm.Productos));
        await accounts.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(8));
        vm.SolicitarEnvioCommand.Execute(null);
        var pending = vm.ConfirmarEnvioCommand.ExecuteAsync(null);
        Assert.True(vm.Enviando);
        Assert.Equal(0, sending.Count);
        accounts.Release.TrySetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, sending.Count);
        Assert.False(vm.ResultadoIncierto);
    }

    [Fact]
    public async Task Uncertain_send_keeps_identical_intention_until_human_repeats()
    {
        var sending = new SendFake();
        var returned = false;
        using var vm = new PedidosSalonViewModel(1, null, "Mesa 1", new CatalogFake(), new AccountFake(),
            () => returned = true, sending, 7);
        vm.AgregarCommand.Execute(Assert.Single(vm.Productos));
        Assert.True(vm.PuedeEnviar);
        vm.SolicitarEnvioCommand.Execute(null);
        Assert.True(vm.ConfirmandoEnvio);
        await vm.ConfirmarEnvioCommand.ExecuteAsync(null);
        Assert.True(vm.ResultadoIncierto);
        Assert.Single(sending.Attempts);
        Assert.False(vm.PuedeVolver);
        vm.VolverCommand.Execute(null);
        Assert.False(returned);
        Assert.False(vm.PuedeEnviar);
        Assert.Single(vm.Borrador);
        Assert.Contains("Una consulta no demuestra", vm.Mensaje, StringComparison.Ordinal);
        Assert.Contains("perderá esta intención", vm.Mensaje, StringComparison.Ordinal);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!vm.PuedeRepetir) await Task.Delay(25, timeout.Token);
        await vm.RepetirEnvioCommand.ExecuteAsync(null);
        Assert.Equal(2, sending.Attempts.Count);
        Assert.Same(sending.Attempts[0], sending.Attempts[1]);
        Assert.False(vm.ResultadoIncierto);
        Assert.Empty(vm.Borrador);
        Assert.Equal("Comanda confirmada por la API.", vm.Mensaje);
        Assert.True(vm.PuedeVolver);
    }

    [Fact]
    public void Catalog_and_draft_show_real_data_without_combining_repeated_products()
    {
        var catalog = new CatalogFake();
        var accounts = new AccountFake();
        var returned = false;
        using var vm = new PedidosSalonViewModel(null, 7, "Grupo 7", catalog, accounts, () => returned = true);
        Assert.True(vm.Conectado);
        Assert.Equal((null, 7), accounts.Destination);
        var product = Assert.Single(vm.Productos);
        vm.AgregarCommand.Execute(product);
        vm.AgregarCommand.Execute(product);
        Assert.Equal(2, vm.Borrador.Count);
        vm.Borrador[0].IncrementarCommand.Execute(null);
        Assert.Equal(30.30m, vm.Subtotal);
        vm.ElegirCategoriaCommand.Execute("Bebida");
        Assert.Empty(vm.Productos);
        vm.VolverCommand.Execute(null);
        Assert.True(returned);
    }

    [Fact]
    public void Legacy_account_disables_adding_lines()
    {
        var account = new CuentaPedidoDto(1, 1, null, 1, DateTimeOffset.UtcNow, 5, 0, 5, "Listo", true, "Legada", []);
        using var vm = new PedidosSalonViewModel(1, null, "Mesa 1", new CatalogFake(), new AccountFake(account), () => { });
        Assert.True(vm.CuentaLegada);
        Assert.False(vm.PuedeAgregar);
        vm.AgregarCommand.Execute(Assert.Single(vm.Productos));
        Assert.Empty(vm.Borrador);
    }

    private sealed class CatalogFake : IProductosPedidoApiService
    {
        public Task<PaginaProductosDto> ConsultarAsync(string? cursor = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaginaProductosDto([new ProductoPedidoDto(1, "Prueba DEMO", "Comida", 10.10m, true, 1)], null));
    }

    private sealed class DisconnectCatalogFake : IProductosPedidoApiService
    {
        private int _count;
        public TaskCompletionSource Disconnected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PaginaProductosDto> ConsultarAsync(string? cursor = null, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _count) == 1)
                return Task.FromResult(new PaginaProductosDto([new ProductoPedidoDto(1, "Prueba DEMO", "Comida", 10.10m, true, 1)], null));
            Disconnected.TrySetResult();
            throw new HttpRequestException("Simulated disconnect");
        }
    }

    private sealed class PausedCatalogFake : IProductosPedidoApiService
    {
        public Task<PaginaProductosDto> ConsultarAsync(string? cursor = null, CancellationToken cancellationToken = default) =>
            throw new SessionPausedException();
    }

    private sealed class ExpiredCatalogFake : IProductosPedidoApiService
    {
        public Task<PaginaProductosDto> ConsultarAsync(string? cursor = null, CancellationToken cancellationToken = default) =>
            throw new SessionExpiredException();
    }

    private sealed class UncertainThenRejectedFake : IEnvioPedidoApiService
    {
        public int Count { get; private set; }
        public Task<EnvioConfirmadoPedidoDto> EnviarAsync(IntencionPedido intencion, CancellationToken cancellationToken = default)
        {
            Count++;
            if (Count == 1) throw new IOException("Simulated uncertain result");
            throw new ApiException(System.Net.HttpStatusCode.Forbidden, new ApiProblem(403, "AUTH_PROHIBIDO", null, null, null));
        }
    }

    private sealed class ConflictSendFake : IEnvioPedidoApiService
    {
        public int Count { get; private set; }
        public Task<EnvioConfirmadoPedidoDto> EnviarAsync(IntencionPedido intencion, CancellationToken cancellationToken = default)
        {
            Count++;
            throw new ApiException(System.Net.HttpStatusCode.Conflict,
                new ApiProblem(409, "CATALOGO_CAMBIADO", null, null, null));
        }
    }

    private sealed class IoFailureSendFake : IEnvioPedidoApiService
    {
        public int Count { get; private set; }
        public Task<EnvioConfirmadoPedidoDto> EnviarAsync(IntencionPedido intencion, CancellationToken cancellationToken = default)
        {
            Count++;
            throw new IOException("Simulated interrupted response body");
        }
    }

    private sealed class AccountFake(CuentaPedidoDto? account = null) : ICuentaPedidoApiService
    {
        public (int? Mesa, int? Grupo) Destination { get; private set; }
        public Task<CuentaActivaPedidoDto> ConsultarAsync(int? mesaId, int? grupoId, CancellationToken cancellationToken = default)
        {
            Destination = (mesaId, grupoId);
            return Task.FromResult(new CuentaActivaPedidoDto(account));
        }
    }

    private sealed class SendFake : IEnvioPedidoApiService
    {
        public List<IntencionPedido> Attempts { get; } = [];
        public Task<EnvioConfirmadoPedidoDto> EnviarAsync(IntencionPedido intencion, CancellationToken cancellationToken = default)
        {
            Attempts.Add(intencion);
            if (Attempts.Count == 1) throw new HttpRequestException("Simulated lost response");
            var line = intencion.Lineas[0];
            var detail = new DetallePedidoDto(1, line.IdProducto, line.Nombre, (short)line.Cantidad,
                line.PrecioUnitario, line.PrecioUnitario * line.Cantidad, line.Notas);
            var command = new ComandaPedidoDto(1, 1, DateTimeOffset.UtcNow, "Abierta", 1, [detail]);
            var account = new CuentaPedidoDto(1, intencion.MesaId, intencion.GrupoId, 1, DateTimeOffset.UtcNow,
                detail.Subtotal, 0, detail.Subtotal, "Abierto", false, null, [command]);
            return Task.FromResult(new EnvioConfirmadoPedidoDto(account, 1, 8));
        }
    }

    private sealed class BlockingAccountFake : ICuentaPedidoApiService
    {
        private int _count;
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CuentaActivaPedidoDto> ConsultarAsync(int? mesaId, int? grupoId, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _count) == 2)
            {
                SecondStarted.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return new CuentaActivaPedidoDto(null);
        }
    }

    private sealed class SuccessSendFake : IEnvioPedidoApiService
    {
        public int Count { get; private set; }
        public Task<EnvioConfirmadoPedidoDto> EnviarAsync(IntencionPedido intencion, CancellationToken cancellationToken = default)
        {
            Count++;
            var line = intencion.Lineas[0];
            var detail = new DetallePedidoDto(1, line.IdProducto, line.Nombre, (short)line.Cantidad,
                line.PrecioUnitario, line.PrecioUnitario * line.Cantidad, line.Notas);
            var command = new ComandaPedidoDto(1, 1, DateTimeOffset.UtcNow, "Abierta", 1, [detail]);
            return Task.FromResult(new EnvioConfirmadoPedidoDto(new CuentaPedidoDto(1, intencion.MesaId,
                intencion.GrupoId, 1, DateTimeOffset.UtcNow, detail.Subtotal, 0, detail.Subtotal, "Abierto", false, null, [command]), 1, 8));
        }
    }
}
