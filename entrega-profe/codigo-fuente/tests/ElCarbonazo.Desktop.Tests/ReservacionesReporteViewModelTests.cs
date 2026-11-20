using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class ReservacionesReporteViewModelTests
{
    [Fact]
    public async Task Agenda_hides_cancelled_items_and_counts_only_active_reservations()
    {
        var now = DateTimeOffset.UtcNow;
        var api = new FakeReservaciones
        {
            Items =
            [
                new ReservacionDto(1, 1, 1, "Activa", 2, now.AddDays(1), now.AddDays(1).AddHours(1), "Activa", now),
                new ReservacionDto(2, 2, 2, "Cancelada", 2, now.AddDays(1), now.AddDays(1).AddHours(1), "Cancelada", now)
            ]
        };
        using var vm = new ReservacionesViewModel(new FakeSession(), new ServerClock(), () => { }, () => { }, api);
        await vm.ConsultarCommand.ExecuteAsync(null);
        Assert.Equal("Activa", Assert.Single(vm.Reservaciones).Estado);
        Assert.Contains("Reservas activas: 1", vm.Mensaje);
    }

    [Fact]
    public async Task Reservation_write_uncertainty_blocks_another_write_and_closing()
    {
        var session = new FakeSession();
        var api = new FakeReservaciones();
        var closed = false;
        using var vm = new ReservacionesViewModel(session, new ServerClock(), () => closed = true, () => { }, api);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        vm.MesaDisponible = new DisponibilidadMesaDto(1, 1, 4);
        vm.NombreReferencia = "Prueba";
        await vm.CrearCommand.ExecuteAsync(null);
        Assert.Equal(1, api.Writes);
        Assert.True(vm.ResultadoIncierto);
        Assert.False(vm.PuedeEscribir);
        Assert.Contains(nameof(vm.PuedeEscribir), changes);
        await vm.CrearCommand.ExecuteAsync(null);
        vm.CerrarCommand.Execute(null);
        Assert.Equal(1, api.Writes);
        Assert.False(closed);
    }

    [Fact]
    public async Task Report_converts_managua_range_and_displays_results()
    {
        var api = new FakeReporte();
        var vm = new ReporteVentasViewModel(new FakeSession(), new ServerClock(), () => { }, api)
        {
            DesdeLocal = "2026-01-01 00:00",
            HastaLocal = "2026-01-02 00:00"
        };
        await vm.ConsultarCommand.ExecuteAsync(null);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero), api.From);
        Assert.Equal(120m, vm.TotalCobrado);
        Assert.Equal(1, vm.CantidadCobros);
        Assert.Single(vm.Productos);
        vm.HastaLocal = vm.DesdeLocal;
        await vm.ConsultarCommand.ExecuteAsync(null);
        Assert.Equal(1, api.Calls);
        Assert.Contains("período", vm.Mensaje);
    }

    private sealed class FakeReservaciones : IReservacionesApiService
    {
        public int Writes { get; private set; }
        public IReadOnlyList<ReservacionDto> Items { get; init; } = [];
        public Task<IReadOnlyList<DisponibilidadMesaDto>> DisponibilidadAsync(DateTimeOffset desde, DateTimeOffset hasta, int personas, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DisponibilidadMesaDto>>([]);
        public Task<IReadOnlyList<ReservacionDto>> ListarAsync(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct) =>
            Task.FromResult(Items);
        public Task<ReservacionDto> CrearAsync(CrearReservacionRequest request, CancellationToken ct)
        {
            Writes++;
            throw new HttpRequestException("desconexión simulada");
        }
        public Task<ReservacionDto> CancelarAsync(int id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeReporte : IReporteVentasApiService
    {
        public int Calls { get; private set; }
        public DateTimeOffset From { get; private set; }
        public Task<ReporteVentasDto> ObtenerAsync(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
        {
            Calls++;
            From = desde;
            return Task.FromResult(new ReporteVentasDto(desde, hasta, 1, 120m, 120m, 0m,
                [new ReporteProductoDto("Plato", 1, 120m)]));
        }
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
