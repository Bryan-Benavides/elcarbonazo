using System.Net;
using System.Text.Json;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class PedidosSalonAnulacionTests
{
    [Fact]
    public async Task Opening_and_cancelling_modal_notifies_button_states_without_sending()
    {
        using var fixture = new JournalFixture();
        var api = new ScriptedAnulacionApi();
        using var vm = BuildVm(new MutableAccountFake(Account()), fixture.Journal, api);
        await WaitAsync(() => vm.PuedeAnular, "la anulación debe habilitarse");
        var changed = new List<string?>();
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        vm.ComandasAnulables[0].Lineas[0].SolicitarCommand.Execute(null);
        Assert.True(vm.PuedeCancelarAnulacion);
        Assert.Contains(nameof(vm.PuedeCancelarAnulacion), changed);
        vm.MotivoAnulacion = "Pedido duplicado";
        vm.EmailJefa = "jefa@example.local";
        vm.ContrasenaJefa = "clave";
        Assert.True(vm.PuedeConfirmarAnulacion);
        Assert.Contains(nameof(vm.PuedeConfirmarAnulacion), changed);
        vm.CancelarAnulacionCommand.Execute(null);
        Assert.False(vm.ConfirmandoAnulacion);
        Assert.Equal("", vm.ContrasenaJefa);
        Assert.Equal(0, api.AuthorizeCount);
    }

    [Fact]
    public async Task Missing_journal_disables_the_flow_without_sending_credentials()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"anulacion-vm-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var api = new ScriptedAnulacionApi();
            var journal = new AnulacionJournal(Path.Combine(directory, "anulaciones-journal.json"),
                Guid.NewGuid(), new TestPolicy());
            using var vm = BuildVm(new MutableAccountFake(Account()), journal, api);
            await WaitAsync(() => vm.Conectado, "la cuenta debe consultarse");
            Assert.False(vm.DiarioAnulacionDisponible);
            Assert.False(vm.PuedeAnular);
            Assert.Contains("diario", vm.MensajeAnulacion, StringComparison.OrdinalIgnoreCase);
            var line = Assert.Single(Assert.Single(vm.ComandasAnulables).Lineas);
            line.SolicitarCommand.Execute(null);
            Assert.False(vm.ConfirmandoAnulacion);
            Assert.Equal(0, api.AuthorizeCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Confirmation_authorizes_once_clears_password_and_waits_for_reconciliation()
    {
        using var fixture = new JournalFixture();
        var account = new MutableAccountFake(Account());
        var api = new ScriptedAnulacionApi
        {
            OnExecute = _ => Task.FromResult(new ResultadoAnulacionDto("Completada", "ANULACION_COMPLETADA", 7, 10.10m, 0, 0, 11))
        };
        using var vm = BuildVm(account, fixture.Journal, api);
        await WaitAsync(() => vm.PuedeAnular, "la anulación debe habilitarse con diario y sesión");

        var command = Assert.Single(vm.ComandasAnulables);
        Assert.Equal("Comanda #21 · Abierta", command.Titulo);
        var line = Assert.Single(command.Lineas);
        line.SolicitarCommand.Execute(null);
        Assert.True(vm.ConfirmandoAnulacion);
        Assert.Contains("Prueba DEMO", vm.ResumenAnulacion, StringComparison.Ordinal);
        Assert.False(vm.PuedeConfirmarAnulacion);

        vm.MotivoAnulacion = "Pedido duplicado por error";
        vm.EmailJefa = "jefa@example.local";
        vm.ContrasenaJefa = "correct horse battery staple";
        Assert.True(vm.PuedeConfirmarAnulacion);
        await vm.ConfirmarAnulacionCommand.ExecuteAsync(null);

        Assert.Equal(1, api.AuthorizeCount);
        Assert.Equal(1, api.ExecuteCount);
        Assert.Equal(1L, api.LastGeneration);
        Assert.Equal("", vm.ContrasenaJefa);
        Assert.False(vm.ConfirmandoAnulacion);
        Assert.False(vm.AnulacionIncierta);
        Assert.True(vm.AnulacionTerminalPendienteConciliar);
        Assert.False(vm.PuedeAnular);
        Assert.Contains("completada", vm.MensajeAnulacion, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("TerminalPorConciliar", fixture.Journal.Read()?.Estado);
        Assert.DoesNotContain("correct horse", File.ReadAllText(fixture.Path), StringComparison.Ordinal);

        account.Current = Account(idPedido: 31, idComanda: 22, idDetalle: 12, total: 5m);
        await WaitAsync(() => !vm.AnulacionTerminalPendienteConciliar, "el diario debe conciliarse con la cuenta");
        Assert.Equal("SinPendientes", fixture.Journal.Read()?.Estado);
        Assert.True(vm.PuedeAnular);
    }

    [Fact]
    public async Task Uncertain_result_blocks_new_annulments_but_allows_leaving_and_barrier()
    {
        using var fixture = new JournalFixture();
        var api = new ScriptedAnulacionApi
        {
            OnExecute = _ => throw new HttpRequestException("Respuesta perdida"),
            OnConsult = _ => throw new ApiException(HttpStatusCode.NotFound,
                new ApiProblem(404, "RECURSO_NO_ENCONTRADO", null, null, null)),
            OnBarrier = (_, _) => Task.FromResult(new OperacionAnulacionDto("Cancelada", "OPERACION_CANCELADA", "{}"))
        };
        using var vm = BuildVm(new MutableAccountFake(Account()), fixture.Journal, api);
        await WaitAsync(() => vm.PuedeAnular, "la anulación debe habilitarse");
        vm.ComandasAnulables[0].Lineas[0].SolicitarCommand.Execute(null);
        vm.MotivoAnulacion = "Pedido duplicado por error";
        vm.EmailJefa = "jefa@example.local";
        vm.ContrasenaJefa = "clave";
        await vm.ConfirmarAnulacionCommand.ExecuteAsync(null);

        Assert.True(vm.AnulacionIncierta);
        Assert.True(vm.PuedeVolver);
        Assert.False(vm.PuedeAnular);
        Assert.False(vm.PuedeEnviar);
        Assert.Equal("Pendiente", fixture.Journal.Read()?.Estado);
        Assert.Equal(1, api.ExecuteCount);

        await vm.ConsultarOperacionAnulacionCommand.ExecuteAsync(null);
        Assert.True(vm.AnulacionIncierta);
        Assert.Equal(1, api.ExecuteCount);

        vm.SolicitarBarreraCommand.Execute(null);
        Assert.True(vm.ConfirmandoBarreraVisible);
        await vm.ConfirmarBarreraCommand.ExecuteAsync(null);
        Assert.False(vm.AnulacionIncierta);
        Assert.False(vm.PuedeRecuperarAnulacion);
        Assert.Equal(1, api.ExecuteCount);

        await WaitAsync(() => !vm.AnulacionTerminalPendienteConciliar, "la barrera debe conciliarse con la cuenta");
        Assert.Equal("SinPendientes", fixture.Journal.Read()?.Estado);
        Assert.True(vm.PuedeAnular);
        Assert.Equal(1, api.ExecuteCount);
    }

    [Fact]
    public async Task Changed_account_before_confirmation_sends_nothing()
    {
        using var fixture = new JournalFixture();
        var api = new ScriptedAnulacionApi();
        var accounts = new MutableAccountFake(Account());
        using var vm = BuildVm(accounts, fixture.Journal, api);
        await WaitAsync(() => vm.PuedeAnular, "la anulación debe habilitarse");
        vm.ComandasAnulables[0].Lineas[0].SolicitarCommand.Execute(null);
        vm.MotivoAnulacion = "Pedido duplicado por error";
        vm.EmailJefa = "jefa@example.local";
        vm.ContrasenaJefa = "clave";

        accounts.Current = Account(idPedido: 99, idComanda: 88, idDetalle: 77, total: 3m);
        vm.Cuenta = accounts.Current;
        await vm.ConfirmarAnulacionCommand.ExecuteAsync(null);

        Assert.Equal(0, api.AuthorizeCount);
        Assert.Equal(0, api.ExecuteCount);
        Assert.Equal("SinPendientes", fixture.Journal.Read()?.Estado);
        Assert.Contains("cambió", vm.MensajeAnulacion, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("", vm.ContrasenaJefa);
    }

    [Fact]
    public async Task Deterministic_rejection_is_terminal_and_never_leaves_the_password()
    {
        using var fixture = new JournalFixture();
        var api = new ScriptedAnulacionApi
        {
            OnExecute = _ => throw new ApiException(HttpStatusCode.Conflict,
                new ApiProblem(409, "CUENTA_YA_PAGADA", null, null, null))
        };
        using var vm = BuildVm(new MutableAccountFake(Account()), fixture.Journal, api);
        await WaitAsync(() => vm.PuedeAnular, "la anulación debe habilitarse");
        Assert.Single(vm.ComandasAnulables).SolicitarCommand!.Execute(null);
        vm.MotivoAnulacion = "Pedido duplicado por error";
        vm.EmailJefa = "jefa@example.local";
        vm.ContrasenaJefa = "clave";
        await vm.ConfirmarAnulacionCommand.ExecuteAsync(null);

        Assert.False(vm.AnulacionIncierta);
        Assert.True(vm.AnulacionTerminalPendienteConciliar);
        Assert.Equal("", vm.ContrasenaJefa);
        Assert.Contains("pagada", vm.MensajeAnulacion, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, api.ExecuteCount);
    }

    [Fact]
    public async Task Role_without_permission_cannot_open_the_modal()
    {
        using var fixture = new JournalFixture();
        var api = new ScriptedAnulacionApi();
        using var vm = BuildVm(new MutableAccountFake(Account()), fixture.Journal, api, role: "Personal de Cocina");
        await WaitAsync(() => vm.Conectado, "la cuenta debe consultarse");
        Assert.False(vm.PuedeAnular);
        vm.ComandasAnulables[0].Lineas[0].SolicitarCommand.Execute(null);
        Assert.False(vm.ConfirmandoAnulacion);
        Assert.Equal(0, api.AuthorizeCount);
    }

    private static PedidosSalonViewModel BuildVm(ICuentaPedidoApiService accounts, AnulacionJournal journal,
        IAnulacionApiService api, string role = "Mesero") =>
        new(1, null, "Mesa 1", new CatalogFake(), accounts, () => { }, null, 7,
            new AnulacionCoordinator(journal, api, () => 7, () => 1, () => false), () => role);

    private static CuentaPedidoDto Account(int idPedido = 31, int idComanda = 21, int idDetalle = 11,
        decimal total = 10.10m, string estado = "Abierto")
    {
        var detail = new DetallePedidoDto(idDetalle, 1, "Prueba DEMO", 1, total, total, "");
        var command = new ComandaPedidoDto(idComanda, 1, DateTimeOffset.UtcNow, "Abierta", 1, [detail]);
        return new CuentaPedidoDto(idPedido, 1, null, 1, DateTimeOffset.UtcNow, total, 0, total, estado,
            false, null, [command]);
    }

    private static async Task WaitAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25);
        Assert.True(condition(), because);
    }

    private sealed class CatalogFake : IProductosPedidoApiService
    {
        public Task<PaginaProductosDto> ConsultarAsync(string? cursor = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaginaProductosDto([new ProductoPedidoDto(1, "Prueba DEMO", "Comida", 10.10m, true, 1)], null));
    }

    private sealed class MutableAccountFake(CuentaPedidoDto? current) : ICuentaPedidoApiService
    {
        public CuentaPedidoDto? Current { get; set; } = current;

        public Task<CuentaActivaPedidoDto> ConsultarAsync(int? mesaId, int? grupoId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CuentaActivaPedidoDto(Current));
    }

    private sealed class ScriptedAnulacionApi : IAnulacionApiService
    {
        public Func<AutorizarAnulacionRequest, Task<AutorizarAnulacionResponse>>? OnAuthorize { get; init; }
        public Func<EjecutarAnulacionRequest, Task<ResultadoAnulacionDto>>? OnExecute { get; init; }
        public Func<Guid, Task<OperacionAnulacionDto>>? OnConsult { get; init; }
        public Func<Guid, bool, Task<OperacionAnulacionDto>>? OnBarrier { get; init; }
        public int AuthorizeCount { get; private set; }
        public int ExecuteCount { get; private set; }
        public long LastGeneration { get; private set; }

        public Task<AutorizarAnulacionResponse> AutorizarAsync(long expectedGeneration,
            AutorizarAnulacionRequest request, CancellationToken cancellationToken = default)
        {
            AuthorizeCount++;
            Assert.Equal(1L, expectedGeneration);
            if (OnAuthorize is not null) return OnAuthorize(request);
            return Task.FromResult(new AutorizarAnulacionResponse("token", DateTimeOffset.UtcNow.AddMinutes(2)));
        }

        public Task<ResultadoAnulacionDto> EjecutarAsync(long expectedGeneration, int idPedido,
            EjecutarAnulacionRequest request, CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            LastGeneration = expectedGeneration;
            return OnExecute is not null
                ? OnExecute(request)
                : Task.FromResult(new ResultadoAnulacionDto("Completada", "ANULACION_COMPLETADA"));
        }

        public Task<OperacionAnulacionDto> ConsultarAsync(Guid idOperacion, CancellationToken cancellationToken = default) =>
            OnConsult is not null
                ? OnConsult(idOperacion)
                : throw new InvalidOperationException("Consulta no configurada.");

        public Task<OperacionAnulacionDto> BarreraAsync(long expectedGeneration, Guid idOperacion,
            bool intervencionAjena, CancellationToken cancellationToken = default) =>
            OnBarrier is not null
                ? OnBarrier(idOperacion, intervencionAjena)
                : throw new InvalidOperationException("Barrera no configurada.");
    }

    private sealed class JournalFixture : IDisposable
    {
        private readonly string _directory;
        public string Path { get; }
        public AnulacionJournal Journal { get; }

        public JournalFixture()
        {
            _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"anulacion-vm-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "anulaciones-journal.json");
            var installation = Guid.NewGuid();
            File.WriteAllBytes(Path, JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1,
                instalacion = installation,
                estado = "SinPendientes",
                operacion = (Guid?)null,
                propietario = (int?)null
            }));
            File.WriteAllText(Path + ".lock", "");
            Journal = new AnulacionJournal(Path, installation, new TestPolicy());
        }

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); }
            catch (IOException) { }
        }
    }

    private sealed class TestPolicy : ICajaRecoverySecurityPolicy
    {
        public bool IsProtected(string path) => true;
    }
}
