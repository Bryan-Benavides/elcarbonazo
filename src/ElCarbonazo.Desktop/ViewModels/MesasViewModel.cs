using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;

namespace ElCarbonazo.Desktop.ViewModels;

public partial class MesasViewModel : ViewModelBase, IDisposable
{
    private readonly IMesasApiService _mesasService;
    private readonly ISessionManager _session;
    private readonly Func<Action, Task> _dispatch;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _resume = new(0, 1);
    private readonly SemaphoreSlim _refresh = new(0, 1);
    private readonly MesasRequestCoordinator _coordinator = new();
    private bool _disposed;

    [ObservableProperty] private string _mensajeAccion = "";
    [ObservableProperty] private bool _confirmarLiberacion;
    [ObservableProperty] private bool _resultadoIncierto;
    [ObservableProperty] private bool _consultaTrasIncertidumbre;
    [ObservableProperty] private bool _mutando;

    public bool PuedeReservar => PuedeCambiar("Reservada");
    public bool PuedeOcupar => PuedeCambiar("Ocupada");
    public bool PuedeLiberar => PuedeCambiar("Libre");
    public bool PuedeReconocer => ResultadoIncierto && ConsultaTrasIncertidumbre;

    private bool PuedeCambiar(string destino) => !_disposed && !Mutando && !ConfirmarLiberacion &&
        _coordinator.CanMutate && Rol is "Jefa" or "Encargado de Caja" or "Mesero" or "Supervisor" &&
        MesaSeleccionada is { IdGrupo: null } mesa && (mesa.Estado, destino) is
            ("Libre", "Reservada") or ("Libre", "Ocupada") or ("Reservada", "Ocupada") or
            ("Reservada", "Libre") or ("Ocupada", "Libre");

    private void ActualizarAcciones()
    {
        OnPropertyChanged(nameof(PuedeReservar));
        OnPropertyChanged(nameof(PuedeOcupar));
        OnPropertyChanged(nameof(PuedeLiberar));
        OnPropertyChanged(nameof(PuedeReconocer));
    }

    partial void OnMesaSeleccionadaChanged(MesaDto? value) { ConfirmarLiberacion = false; ActualizarAcciones(); }
    partial void OnConfirmarLiberacionChanged(bool value) => ActualizarAcciones();
    partial void OnResultadoInciertoChanged(bool value) => ActualizarAcciones();
    partial void OnConsultaTrasIncertidumbreChanged(bool value) => ActualizarAcciones();
    partial void OnMutandoChanged(bool value) => ActualizarAcciones();

    [RelayCommand] private Task ReservarAsync() => CambiarEstadoAsync("Reservada");
    [RelayCommand] private Task OcuparAsync() => CambiarEstadoAsync("Ocupada");
    [RelayCommand] private void SolicitarLiberacion() { if (PuedeLiberar) ConfirmarLiberacion = true; }
    [RelayCommand] private void CancelarLiberacion() => ConfirmarLiberacion = false;
    [RelayCommand]
    private async Task LiberarAsync()
    {
        if (!ConfirmarLiberacion) return;
        ConfirmarLiberacion = false;
        await CambiarEstadoAsync("Libre");
    }

    [RelayCommand]
    private void ReconocerIncertidumbre()
    {
        if (!_coordinator.AcknowledgeUncertainty()) return;
        ResultadoIncierto = false;
        MensajeAccion = "Revisión reconocida. Verifique la mesa antes de realizar otra acción.";
        ActualizarAcciones();
    }

    private void SolicitarConsulta()
    {
        if (_refresh.CurrentCount == 0)
            try { _refresh.Release(); } catch (SemaphoreFullException) { }
    }

    private async Task CambiarEstadoAsync(string destino)
    {
        if (!PuedeCambiar(destino) || MesaSeleccionada is not { } mesa || !_coordinator.TryBeginMutation()) return;
        Mutando = true;
        MensajeAccion = "";
        bool reconciliar = false, incierto = false;
        MesaDto? resultado = null;
        try
        {
            resultado = await _mesasService.UpdateEstadoAsync(mesa.IdMesa, destino, mesa.Version, _lifetime.Token);
        }
        catch (ApiException ex) when ((int)ex.StatusCode < 500)
        {
            reconciliar = (int)ex.StatusCode is not (401 or 403);
            MensajeAccion = ex.Problem?.Code switch
            {
                "MESA_CONFLICTO_CONCURRENCIA" => "La mesa cambió en otra terminal. Revise los datos actualizados antes de volver a intentar.",
                "MESA_CON_PEDIDO_PENDIENTE" => "No se puede liberar: la mesa tiene una cuenta pendiente.",
                "MESA_PERTENECE_A_GRUPO" => "La mesa pertenece a un grupo; no puede cambiarse individualmente.",
                _ when (int)ex.StatusCode is 401 or 403 => "No tiene autorización para realizar esta acción.",
                _ => $"La API rechazó el cambio: {ex.Message}"
            };
        }
        catch (SessionPausedException) { MensajeAccion = "Sesión en pausa. Interactúe para continuar."; }
        catch (SessionExpiredException) { }
        catch (SecurityConnectionException) { }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or ApiException or System.Text.Json.JsonException or InvalidDataException)
        {
            incierto = true;
            ResultadoIncierto = true;
            ConsultaTrasIncertidumbre = false;
            MensajeAccion = "Resultado incierto: el cambio pudo aplicarse. Revise las mesas; una consulta no demuestra que la escritura haya terminado. No se reintentó automáticamente.";
        }
        finally
        {
            _coordinator.CompleteMutation(reconciliar, incierto, () =>
            {
                if (resultado is null) return;
                var index = Mesas.ToList().FindIndex(m => m.IdMesa == resultado.IdMesa);
                if (index >= 0) Mesas[index] = resultado;
                if (MesaSeleccionada?.IdMesa == resultado.IdMesa) MesaSeleccionada = resultado;
                MensajeAccion = "Estado actualizado.";
            });
            Mutando = false;
            ActualizarAcciones();
            if (reconciliar || incierto) SolicitarConsulta();
        }
    }

    public string Nombre { get; }
    public string Rol { get; }
    public ObservableCollection<MesaDto> Mesas { get; } = [];

    [ObservableProperty]
    private string _estadoConexion = "Cargando mesas…";

    [ObservableProperty]
    private bool _confirmarCierre;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMesaSeleccionada))]
    [NotifyPropertyChangedFor(nameof(DetalleTitulo))]
    [NotifyPropertyChangedFor(nameof(DetalleCapacidad))]
    [NotifyPropertyChangedFor(nameof(DetalleEstado))]
    [NotifyPropertyChangedFor(nameof(DetalleGrupo))]
    private MesaDto? _mesaSeleccionada;

    public bool HayMesaSeleccionada => MesaSeleccionada is not null;
    public string DetalleTitulo => MesaSeleccionada is { } mesa ? $"Mesa {mesa.Numero:00}" : string.Empty;
    public string DetalleCapacidad => MesaSeleccionada is { } mesa ? $"Capacidad: {mesa.Capacidad} personas" : string.Empty;
    public string DetalleEstado => MesaSeleccionada is { } mesa ? $"Estado: {mesa.Estado}" : string.Empty;
    public string DetalleGrupo => MesaSeleccionada?.IdGrupo is int id
        ? $"Grupo {id}: mesas {string.Join(", ", Mesas.Where(m => m.IdGrupo == id).Select(m => m.Numero).Order())}"
        : "Sin grupo";

    [RelayCommand]
    private void SeleccionarMesa(MesaDto? mesa)
    {
        MesaSeleccionada = mesa is null || MesaSeleccionada?.IdMesa == mesa.IdMesa
            ? null
            : Mesas.FirstOrDefault(m => m.IdMesa == mesa.IdMesa);
    }

    [RelayCommand]
    private void CerrarDetalle() => MesaSeleccionada = null;

    [RelayCommand]
    private void SolicitarCierre() => ConfirmarCierre = true;

    [RelayCommand]
    private void CancelarCierre() => ConfirmarCierre = false;

    [RelayCommand]
    private async Task CerrarSesionAsync()
    {
        ConfirmarCierre = false;
        _lifetime.Cancel();
        await _session.LogoutAsync();
    }

    public MesasViewModel(string nombre, string rol, ISessionManager session, IMesasApiService mesasService,
        Func<Action, Task>? dispatch = null)
    {
        Nombre = nombre;
        Rol = rol;
        _session = session;
        _mesasService = mesasService;
        _dispatch = dispatch ?? DispatchToUiAsync;
        _coordinator.SetSessionActive(session.State is SessionLifecycleState.Active or SessionLifecycleState.Expiring);
        _session.SessionStateChanged += OnSessionStateChanged;
        _ = PollAsync(_lifetime.Token);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        int failures = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_session.State == SessionLifecycleState.Paused)
                {
                    await _dispatch(() => EstadoConexion = "Sesión en pausa. Interactúe para continuar.");
                    await _resume.WaitAsync(cancellationToken);
                    continue;
                }
                if (_session.State is not (SessionLifecycleState.Active or SessionLifecycleState.Expiring))
                {
                    return;
                }

                var timer = Stopwatch.StartNew();
                var ticket = _coordinator.TryBeginRead();
                if (ticket is null)
                {
                    await _refresh.WaitAsync(MesasPollingPolicy.Interval, cancellationToken);
                    continue;
                }
                TimeSpan delay;
                try
                {
                    var mesas = await _mesasService.GetAsync(cancellationToken);
                    if (cancellationToken.IsCancellationRequested ||
                        _session.State is not (SessionLifecycleState.Active or SessionLifecycleState.Expiring))
                    {
                        continue;
                    }
                    await _dispatch(() =>
                    {
                        if (cancellationToken.IsCancellationRequested ||
                            _session.State is not (SessionLifecycleState.Active or SessionLifecycleState.Expiring)) return;
                        _coordinator.CompleteRead(ticket.Value, true, () =>
                        {
                            var selectedId = MesaSeleccionada?.IdMesa;
                            Mesas.Clear();
                            foreach (var mesa in mesas.OrderBy(m => m.Numero)) Mesas.Add(mesa);
                            if (selectedId is int id)
                            {
                                MesaSeleccionada = Mesas.FirstOrDefault(m => m.IdMesa == id);
                                OnPropertyChanged(nameof(DetalleGrupo));
                            }
                            EstadoConexion = "Conectado";
                            if (ResultadoIncierto) ConsultaTrasIncertidumbre = true;
                            ActualizarAcciones();
                        });
                    });
                    failures = 0;
                    delay = MesasPollingPolicy.DelayAfterSuccess(timer.Elapsed);
                }
                catch (SessionPausedException)
                {
                    continue;
                }
                catch (SessionExpiredException)
                {
                    return;
                }
                catch (SecurityConnectionException)
                {
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (ApiException ex) when ((int)ex.StatusCode is 401 or 403)
                {
                    await _dispatch(() =>
                    {
                        _coordinator.CompleteRead(ticket.Value, false);
                        EstadoConexion = "No está autorizado para consultar mesas.";
                        ActualizarAcciones();
                    });
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or ApiException or System.Text.Json.JsonException or InvalidDataException)
                {
                    await _dispatch(() =>
                    {
                        if (_coordinator.CompleteRead(ticket.Value, false))
                        {
                            EstadoConexion = "Sin conexión o respuesta inválida. Mostrando últimos datos.";
                            ActualizarAcciones();
                        }
                    });
                    failures = Math.Min(failures + 1, 4);
                    delay = MesasPollingPolicy.DelayAfterFailure(failures);
                }
                finally
                {
                    _coordinator.CompleteRead(ticket.Value, false);
                }
                await _refresh.WaitAsync(delay, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task DispatchToUiAsync(Action action) => await Dispatcher.UIThread.InvokeAsync(action);

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs args)
    {
        _coordinator.SetSessionActive(args.State is SessionLifecycleState.Active or SessionLifecycleState.Expiring);
        _ = _dispatch(ActualizarAcciones);
        if (args.PreviousState == SessionLifecycleState.Paused &&
            args.State is SessionLifecycleState.Active or SessionLifecycleState.Expiring)
        {
            if (_resume.CurrentCount == 0)
            {
                try { _resume.Release(); } catch (SemaphoreFullException) { }
            }
        }
        else if (args.State is SessionLifecycleState.SignedOut or SessionLifecycleState.Expired or SessionLifecycleState.SecurityBlocked or SessionLifecycleState.ShuttingDown)
        {
            _lifetime.Cancel();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.SessionStateChanged -= OnSessionStateChanged;
        _coordinator.Stop();
        _lifetime.Cancel();
    }
}
