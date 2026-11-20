using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;

namespace ElCarbonazo.Desktop.ViewModels;

/// <summary>Cola KDS: lectura y escritura serializadas, sin reintento automático.</summary>
public partial class CocinaViewModel : ViewModelBase, IDisposable
{
    private readonly ISessionManager _session;
    private readonly ICocinaApiService _api;
    private readonly Action _volver;
    private readonly Func<Action, Task> _dispatch;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _operacion = new(1, 1);
    private readonly SemaphoreSlim _reanudar = new(0, 1);
    private bool _disposed;

    [ObservableProperty] private string _estadoConexion = "Consultando cocina…";
    [ObservableProperty] private string _mensaje = "";
    [ObservableProperty] private bool _conectado;
    [ObservableProperty] private bool _mutando;
    [ObservableProperty] private bool _resultadoIncierto;
    [ObservableProperty] private bool _hayMas;

    public ObservableCollection<CocinaComandaDto> Comandas { get; } = [];
    public ObservableCollection<AnulacionKdsDto> AnulacionesPendientes { get; } = [];
    public bool ColaVacia => Conectado && Comandas.Count == 0;
    public bool PuedeOperar => !_disposed && Conectado && !Mutando && !ResultadoIncierto && SesionActiva;
    public bool PuedeVolver => !Mutando && !ResultadoIncierto;
    private bool SesionActiva => _session.State is SessionLifecycleState.Active or SessionLifecycleState.Expiring;

    public CocinaViewModel(ISessionManager session, ICocinaApiService api, Action volver, Func<Action, Task>? dispatch = null)
    {
        _session = session;
        _api = api;
        _volver = volver;
        _dispatch = dispatch ?? (async action => await Dispatcher.UIThread.InvokeAsync(action));
        _session.SessionStateChanged += AlCambiarSesion;
        _ = ConsultarPeriodicamenteAsync(_lifetime.Token);
    }

    partial void OnConectadoChanged(bool value) => ActualizarPermisos();
    partial void OnMutandoChanged(bool value) => ActualizarPermisos();
    partial void OnResultadoInciertoChanged(bool value) => ActualizarPermisos();

    private void ActualizarPermisos()
    {
        OnPropertyChanged(nameof(PuedeOperar));
        OnPropertyChanged(nameof(PuedeVolver));
        OnPropertyChanged(nameof(ColaVacia));
    }

    [RelayCommand] private void Volver() { if (PuedeVolver) _volver(); }

    [RelayCommand]
    private async Task AvanzarAsync(CocinaComandaDto? seleccion)
    {
        if (!PuedeOperar || seleccion is null || !Comandas.Contains(seleccion)) return;
        Mutando = true;
        var adquirida = false;
        try
        {
            await _operacion.WaitAsync(_lifetime.Token);
            adquirida = true;
            // La lectura anterior puede haber cambiado la tarjeta mientras esperábamos.
            if (!Conectado || !SesionActiva || !Comandas.Contains(seleccion))
            {
                Mensaje = "La cola cambió antes de enviar. Revise la comanda y decida nuevamente.";
                return;
            }
            var actualizada = await _api.CambiarEstadoAsync(seleccion, _lifetime.Token);
            if (_lifetime.IsCancellationRequested || !SesionActiva) return;
            var index = Comandas.IndexOf(seleccion);
            if (index >= 0)
            {
                if (actualizada.Estado == "Lista") Comandas.RemoveAt(index);
                else Comandas[index] = actualizada;
            }
            Mensaje = actualizada.Estado == "Lista" ? "Comanda marcada como lista; no significa que haya sido retirada." : "Comanda en preparación.";
        }
        catch (ApiException ex) when ((int)ex.StatusCode < 500)
        {
            Conectado = false;
            Mensaje = (int)ex.StatusCode == 409
                ? "La comanda cambió en otra terminal. Revise la cola actualizada antes de decidir otra acción."
                : "La API rechazó el cambio. Revise la sesión, permisos y comanda antes de continuar.";
        }
        catch (SessionPausedException) { Conectado = false; Mensaje = "Sesión en pausa; no se envió el cambio."; }
        catch (SessionExpiredException) { Conectado = false; }
        catch (SecurityConnectionException)
        {
            ResultadoIncierto = true;
            Conectado = false;
            Mensaje = "No se pudo confirmar el cambio por un fallo de seguridad. Una consulta no demuestra que terminó; solicite revisión humana.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            ResultadoIncierto = true;
            Conectado = false;
            Mensaje = "Resultado incierto: el cambio pudo aplicarse. Una consulta no demuestra que terminó. No se reintentó automáticamente; solicite revisión humana. Al cerrar se pierde este bloqueo local.";
        }
        finally
        {
            if (adquirida) _operacion.Release();
            Mutando = false;
            ActualizarPermisos();
        }
    }

    [RelayCommand]
    private async Task AcusarAnulacionAsync(AnulacionKdsDto? alerta)
    {
        if (!PuedeOperar || alerta is null || !AnulacionesPendientes.Contains(alerta)) return;
        Mutando = true;
        var acquired = false;
        var generation = _session is SessionManager manager ? manager.CajaSessionGeneration : 0;
        try
        {
            await _operacion.WaitAsync(_lifetime.Token);
            acquired = true;
            if (!SesionActiva || !AnulacionesPendientes.Contains(alerta) ||
                generation != 0 && _session is SessionManager current && current.CajaSessionGeneration != generation)
            {
                Mensaje = "La sesión o alerta cambió antes del acuse. Revise nuevamente.";
                return;
            }
            await _api.AcusarAnulacionAsync(alerta.IdAnulacion, _lifetime.Token);
            if (_lifetime.IsCancellationRequested || !SesionActiva) return;
            AnulacionesPendientes.Remove(alerta);
            Mensaje = "Alerta acusada. El acuse aplica únicamente a este evento.";
        }
        catch (ApiException ex) when ((int)ex.StatusCode < 500)
        {
            Mensaje = "La API rechazó el acuse. Revise la sesión y la alerta.";
        }
        catch (SessionPausedException) { Mensaje = "Sesión en pausa; no se envió el acuse."; }
        catch (SessionExpiredException) { Conectado = false; }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            ResultadoIncierto = true;
            Conectado = false;
            Mensaje = "El acuse tiene resultado incierto. No se reintentó; solicite revisión humana.";
        }
        finally
        {
            if (acquired) _operacion.Release();
            Mutando = false;
            ActualizarPermisos();
        }
    }

    private async Task ConsultarPeriodicamenteAsync(CancellationToken token)
    {
        var fallos = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (_session.State == SessionLifecycleState.Paused)
                {
                    await _dispatch(() => { Conectado = false; EstadoConexion = "Sesión en pausa. Interactúe para continuar."; });
                    await _reanudar.WaitAsync(token);
                    continue;
                }
                if (_session.State is not (SessionLifecycleState.Active or SessionLifecycleState.Expiring)) return;
                try
                {
                    await _operacion.WaitAsync(token);
                    try
                    {
                        if (!SesionActiva) return;
                        else
                        {
                            var items = new List<CocinaComandaDto>();
                            var cursors = new HashSet<string>(StringComparer.Ordinal);
                            string? cursor = null;
                            do
                            {
                                var pagina = await _api.ConsultarAsync(cursor, token);
                                foreach (var item in pagina.Items)
                                {
                                    if (items.Count > 0 && (item.FechaHora < items[^1].FechaHora ||
                                        item.FechaHora == items[^1].FechaHora && item.IdComanda <= items[^1].IdComanda))
                                        throw new InvalidDataException("Cola FIFO inválida entre páginas.");
                                    items.Add(item);
                                }
                                cursor = pagina.CursorSiguiente;
                                if (cursor is not null && !cursors.Add(cursor)) throw new InvalidDataException("Cursor repetido.");
                            } while (cursor is not null && items.Count < 100);
                            var alerts = new List<AnulacionKdsDto>();
                            var alertCursors = new HashSet<string>(StringComparer.Ordinal);
                            string? alertCursor = null;
                            do
                            {
                                var page = await _api.ConsultarAnulacionesAsync(alertCursor, token);
                                alerts.AddRange(page.Items);
                                alertCursor = page.CursorSiguiente;
                                if (alertCursor is not null && !alertCursors.Add(alertCursor))
                                    throw new InvalidDataException("Cursor repetido en alertas.");
                            } while (alertCursor is not null && alerts.Count < 100);
                            token.ThrowIfCancellationRequested();
                            await _dispatch(() =>
                            {
                                if (token.IsCancellationRequested || !SesionActiva) return;
                                Comandas.Clear();
                                foreach (var item in items) Comandas.Add(item);
                                AnulacionesPendientes.Clear();
                                foreach (var alert in alerts) AnulacionesPendientes.Add(alert);
                                HayMas = cursor is not null;
                                Conectado = true;
                                EstadoConexion = "Cola actualizada · consulta local";
                                ActualizarPermisos();
                            });
                            fallos = 0;
                        }
                    }
                    finally { _operacion.Release(); }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception)
                {
                    await _dispatch(() => { Conectado = false; EstadoConexion = "Sin datos actuales · se conserva la última cola."; });
                    fallos = Math.Min(fallos + 1, 4);
                }
                await Task.Delay(fallos switch { 0 or 1 => 2000, 2 => 5000, 3 => 10000, _ => 30000 }, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void AlCambiarSesion(object? sender, SessionStateChangedEventArgs e)
    {
        _ = _dispatch(() =>
        {
            if (!SesionActiva)
            {
                Conectado = false;
                EstadoConexion = e.State == SessionLifecycleState.Paused
                    ? "Sesión en pausa. Interactúe para continuar."
                    : "Sesión finalizada.";
            }
            ActualizarPermisos();
        });
        if (e.PreviousState == SessionLifecycleState.Paused && SesionActiva && _reanudar.CurrentCount == 0)
            try { _reanudar.Release(); } catch (SemaphoreFullException) { }
        else if (e.State is SessionLifecycleState.SignedOut or SessionLifecycleState.Expired or
                 SessionLifecycleState.SecurityBlocked or SessionLifecycleState.ShuttingDown)
            _lifetime.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.SessionStateChanged -= AlCambiarSesion;
        _lifetime.Cancel();
    }
}
