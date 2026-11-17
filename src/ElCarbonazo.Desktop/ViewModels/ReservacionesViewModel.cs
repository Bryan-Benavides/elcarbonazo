using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;

namespace ElCarbonazo.Desktop.ViewModels;

public partial class ReservacionesViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeZoneInfo Managua = ResolveManagua();
    private readonly IReservacionesApiService _api;
    private readonly IServerClock _clock;
    private readonly ISessionManager _session;
    private readonly Action _close;
    private readonly Action _refreshTables;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    [ObservableProperty] private string _mensaje = "Consulte disponibilidad antes de reservar.";
    [ObservableProperty] private string _desdeLocal = "";
    [ObservableProperty] private string _hastaLocal = "";
    [ObservableProperty] private string _nombreReferencia = "";
    [ObservableProperty] private string _cantidadPersonas = "2";
    [ObservableProperty] private DisponibilidadMesaDto? _mesaDisponible;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeEscribir))]
    private bool _ocupado;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeEscribir))]
    private bool _resultadoIncierto;
    [ObservableProperty] private ReservacionDto? _reservacionParaCancelar;
    [ObservableProperty] private bool _confirmarCancelacion;

    public ObservableCollection<DisponibilidadMesaDto> Disponibles { get; } = [];
    public ObservableCollection<ReservacionDto> Reservaciones { get; } = [];
    public bool PuedeEscribir => !_disposed && !Ocupado && !ResultadoIncierto &&
        _session.State is SessionLifecycleState.Active or SessionLifecycleState.Expiring &&
        _session.CurrentSession?.Rol is "Jefa" or "Supervisor" or "Mesero" or "Encargado de Caja";

    public ReservacionesViewModel(ISessionManager session, IServerClock clock, Action close, Action refreshTables,
        IReservacionesApiService? api = null)
    {
        _session = session;
        _clock = clock;
        _close = close;
        _refreshTables = refreshTables;
        _api = api ?? new ReservacionesApiService(session);
        var local = TimeZoneInfo.ConvertTime(_clock.UtcNow.AddDays(1), Managua);
        local = new DateTimeOffset(local.Year, local.Month, local.Day, 18, 0, 0, local.Offset);
        DesdeLocal = local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        HastaLocal = local.AddHours(2).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private async Task ConsultarAsync(CancellationToken ct)
    {
        if (!TryInterval(out var from, out var until, out var count))
        {
            Mensaje = "Use fechas locales de Managua (AAAA-MM-DD HH:mm), duración máxima de 8 horas y personas entre 1 y 20.";
            return;
        }
        Ocupado = true;
        try
        {
            var available = await _api.DisponibilidadAsync(from, until, count, ct);
            var start = _clock.UtcNow.AddDays(-1);
            var end = start.AddDays(366);
            var bookings = await _api.ListarAsync(start, end, ct);
            Disponibles.Clear();
            foreach (var item in available) Disponibles.Add(item);
            Reservaciones.Clear();
            foreach (var item in bookings.Where(x => x.Estado == "Activa")) Reservaciones.Add(item);
            MesaDisponible = null;
            Mensaje = $"Mesas disponibles: {available.Count}. Reservas activas: {Reservaciones.Count}.";
        }
        catch (ApiException ex) when ((int)ex.StatusCode < 500)
        {
            Mensaje = ex.Problem?.Code switch
            {
                "RESERVACION_CONFLICTO" => "Ese horario acaba de ocuparse. Consulte disponibilidad nuevamente.",
                "MESA_NO_DISPONIBLE" => "La mesa cambió de estado o ya no está disponible.",
                _ => "La API rechazó la consulta. Revise las fechas y el permiso de su rol."
            };
        }
        catch (SessionPausedException) { Mensaje = "La sesión está en pausa; interactúe para continuar."; }
        catch (SessionExpiredException) { Mensaje = "La sesión venció. Inicie sesión nuevamente."; }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || ct.IsCancellationRequested) { }
        catch (Exception)
        {
            Mensaje = "No se pudo consultar reservas. Revise la conexión y vuelva a consultar.";
        }
        finally { Ocupado = false; }
    }

    [RelayCommand]
    private async Task CrearAsync(CancellationToken ct)
    {
        if (!PuedeEscribir || MesaDisponible is not { } table ||
            !TryInterval(out var from, out var until, out var count))
        {
            Mensaje = "Seleccione una mesa disponible y revise los datos antes de reservar.";
            return;
        }
        Ocupado = true;
        try
        {
            var created = await _api.CrearAsync(new(table.IdMesa, NombreReferencia, count, from, until), ct);
            NombreReferencia = "";
            MesaDisponible = null;
            _refreshTables();
            await ConsultarAsync(ct);
            Mensaje = $"Reserva creada para Mesa {created.NumeroMesa:00}.";
        }
        catch (ApiException ex) when ((int)ex.StatusCode < 500)
        {
            var mensaje = ex.Problem?.Code switch
            {
                "RESERVACION_CONFLICTO" or "MESA_NO_DISPONIBLE" => "La mesa u horario dejó de estar disponible. Consulte de nuevo.",
                _ => "La API rechazó la reservación. Revise los datos."
            };
            await ConsultarAsync(ct);
            Mensaje = mensaje;
        }
        catch (SessionPausedException) { Mensaje = "La sesión está en pausa; no se envió la reservación."; }
        catch (SessionExpiredException) { Mensaje = "La sesión venció. Inicie sesión nuevamente."; }
        catch (Exception)
        {
            ResultadoIncierto = true;
            Mensaje = "Resultado incierto: no se reintentó. Consulte la agenda y revise manualmente antes de una nueva acción.";
        }
        finally { Ocupado = false; }
    }

    [RelayCommand]
    private void Cancelar(ReservacionDto? reservation)
    {
        if (!PuedeEscribir || reservation is null || reservation.Estado != "Activa") return;
        ReservacionParaCancelar = reservation;
        ConfirmarCancelacion = true;
    }

    [RelayCommand]
    private void CerrarConfirmacionCancelacion()
    {
        ConfirmarCancelacion = false;
        ReservacionParaCancelar = null;
    }

    [RelayCommand]
    private async Task ConfirmarCancelarAsync(CancellationToken ct)
    {
        var reservation = ReservacionParaCancelar;
        if (!PuedeEscribir || reservation is null || reservation.Estado != "Activa") return;
        ConfirmarCancelacion = false;
        Ocupado = true;
        try
        {
            await _api.CancelarAsync(reservation.IdReservacion, ct);
            _refreshTables();
            await ConsultarAsync(ct);
            Mensaje = $"Reserva de Mesa {reservation.NumeroMesa:00} cancelada.";
        }
        catch (ApiException ex) when ((int)ex.StatusCode < 500)
        {
            await ConsultarAsync(ct);
            Mensaje = "La API rechazó la cancelación. Actualice la agenda antes de decidir.";
        }
        catch (SessionPausedException) { Mensaje = "La sesión está en pausa; no se envió la cancelación."; }
        catch (SessionExpiredException) { Mensaje = "La sesión venció. Inicie sesión nuevamente."; }
        catch (Exception)
        {
            ResultadoIncierto = true;
            Mensaje = "Resultado incierto: no se reintentó. Revise la agenda antes de otra acción.";
        }
        finally { Ocupado = false; }
        ReservacionParaCancelar = null;
    }

    [RelayCommand]
    private async Task ReconocerRevisionAsync(CancellationToken ct)
    {
        if (!ResultadoIncierto) return;
        await ConsultarAsync(ct);
        Mensaje = "La consulta no prueba que la operación anterior haya terminado. No se habilitarán más cambios en esta pantalla durante la sesión.";
    }

    [RelayCommand] private void SeleccionarMesa(DisponibilidadMesaDto? table) => MesaDisponible = table;
    [RelayCommand]
    private void Cerrar()
    {
        if (ResultadoIncierto || Ocupado)
        {
            Mensaje = "Hay una operación pendiente o incierta. No se puede cerrar esta pantalla durante la sesión.";
            return;
        }
        _close();
    }

    private bool TryInterval(out DateTimeOffset fromUtc, out DateTimeOffset untilUtc, out int people)
    {
        fromUtc = default;
        untilUtc = default;
        people = 0;
        if (!int.TryParse(CantidadPersonas, NumberStyles.None, CultureInfo.InvariantCulture, out people) ||
            people is < 1 or > 20 ||
            !DateTime.TryParseExact(DesdeLocal, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromLocal) ||
            !DateTime.TryParseExact(HastaLocal, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var untilLocal) ||
            Managua.IsInvalidTime(fromLocal) || Managua.IsInvalidTime(untilLocal)) return false;
        fromUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(fromLocal, DateTimeKind.Unspecified), Managua), TimeSpan.Zero);
        untilUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(untilLocal, DateTimeKind.Unspecified), Managua), TimeSpan.Zero);
        return fromUtc < untilUtc && untilUtc - fromUtc <= TimeSpan.FromHours(8);
    }

    private static TimeZoneInfo ResolveManagua()
    {
        foreach (var id in new[] { "America/Managua", "Central America Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        throw new InvalidOperationException("No se encontró la zona horaria de Managua.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
