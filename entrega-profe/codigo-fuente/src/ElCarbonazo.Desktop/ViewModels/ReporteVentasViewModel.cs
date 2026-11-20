using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;

namespace ElCarbonazo.Desktop.ViewModels;

public partial class ReporteVentasViewModel : ViewModelBase
{
    private static readonly TimeZoneInfo Managua = ResolveManagua();
    private readonly IReporteVentasApiService _api;
    private readonly IServerClock _clock;
    private readonly Action _close;

    [ObservableProperty] private string _desdeLocal = "";
    [ObservableProperty] private string _hastaLocal = "";
    [ObservableProperty] private string _mensaje = "Seleccione el período y consulte el reporte.";
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private int _cantidadCobros;
    [ObservableProperty] private decimal _totalCobrado;
    [ObservableProperty] private decimal _ticketPromedio;
    [ObservableProperty] private decimal _impuestosRegistrados;

    public ObservableCollection<ReporteProductoDto> Productos { get; } = [];

    public ReporteVentasViewModel(ISessionManager session, IServerClock clock, Action close,
        IReporteVentasApiService? api = null)
    {
        _clock = clock;
        _close = close;
        _api = api ?? new ReporteVentasApiService(session);
        var localNow = TimeZoneInfo.ConvertTime(clock.UtcNow, Managua);
        var monthStart = new DateTimeOffset(localNow.Year, localNow.Month, 1, 0, 0, 0, localNow.Offset);
        DesdeLocal = monthStart.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        HastaLocal = localNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private async Task ConsultarAsync(CancellationToken ct)
    {
        CantidadCobros = 0;
        TotalCobrado = 0;
        TicketPromedio = 0;
        ImpuestosRegistrados = 0;
        Productos.Clear();
        if (!DateTime.TryParseExact(DesdeLocal, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var fromLocal) ||
            !DateTime.TryParseExact(HastaLocal, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var untilLocal) ||
            Managua.IsInvalidTime(fromLocal) || Managua.IsInvalidTime(untilLocal))
        {
            Mensaje = "Use fechas locales de Managua con formato AAAA-MM-DD HH:mm.";
            return;
        }
        var from = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(fromLocal, DateTimeKind.Unspecified), Managua), TimeSpan.Zero);
        var until = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(untilLocal, DateTimeKind.Unspecified), Managua), TimeSpan.Zero);
        if (from >= until || until - from > TimeSpan.FromDays(366))
        {
            Mensaje = "El período debe ser positivo y no superar 366 días.";
            return;
        }
        Ocupado = true;
        try
        {
            var report = await _api.ObtenerAsync(from, until, ct);
            CantidadCobros = report.CantidadCobros;
            TotalCobrado = report.TotalCobrado;
            TicketPromedio = report.TicketPromedio;
            ImpuestosRegistrados = report.ImpuestosRegistrados;
            Productos.Clear();
            foreach (var item in report.Productos) Productos.Add(item);
            Mensaje = report.CantidadCobros == 0 ? "No hay cobros confirmados en el período." : "Reporte actualizado.";
        }
        catch (ApiException ex) when ((int)ex.StatusCode < 500)
        {
            Mensaje = (int)ex.StatusCode == 403 ? "Su rol no puede consultar reportes de ventas." : "La API rechazó el rango de fechas.";
        }
        catch (SessionPausedException) { Mensaje = "La sesión está en pausa; interactúe para continuar."; }
        catch (SessionExpiredException) { Mensaje = "La sesión venció. Inicie sesión nuevamente."; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { Mensaje = "No se pudo consultar el reporte. Revise la conexión e inténtelo de nuevo."; }
        finally { Ocupado = false; }
    }

    [RelayCommand] private void Cerrar() => _close();

    private static TimeZoneInfo ResolveManagua()
    {
        foreach (var id in new[] { "America/Managua", "Central America Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        throw new InvalidOperationException("No se encontró la zona horaria de Managua.");
    }
}
