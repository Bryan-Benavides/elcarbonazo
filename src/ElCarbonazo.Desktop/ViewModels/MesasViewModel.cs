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
    private readonly CajaRecoveryViewModel? _recoveryPanel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrandoRecuperacionCaja))]
    [NotifyPropertyChangedFor(nameof(MostrandoModulo))]
    private CajaRecoveryViewModel? _recuperacionCajaActual;
    public bool MostrandoRecuperacionCaja => RecuperacionCajaActual is not null;
    public bool RolPuedeRecuperarCaja => (Rol is "Jefa" or "Encargado de Caja") && _recoveryPanel is not null;
    public bool PuedeAbrirRecuperacionCaja => RolPuedeRecuperarCaja && !_disposed && !MostrandoModulo && !Mutando && !ResultadoIncierto && !SeleccionandoGrupo && _coordinator.CanMutate;

    [RelayCommand]
    private async Task AbrirRecuperacionCajaAsync()
    {
        if (!PuedeAbrirRecuperacionCaja || _recoveryPanel is null) return;
        Mutando = true;
        bool reserved;
        try { reserved = await _coordinator.TryBeginMutationAsync(_lifetime.Token); }
        catch (OperationCanceledException) { Mutando = false; return; }
        if (!reserved) { Mutando = false; return; }
        if (!_recoveryPanel.Open(CerrarRecuperacionCaja))
        {
            _coordinator.CompleteMutation(requiresReconciliation: true);
            Mutando = false;
            SolicitarConsulta();
            return;
        }
        RecuperacionCajaActual = _recoveryPanel;
        ActualizarAcciones();
    }

    private void CerrarRecuperacionCaja()
    {
        RecuperacionCajaActual = null;
        _coordinator.CompleteMutation(requiresReconciliation: true);
        Mutando = false;
        SolicitarConsulta();
        ActualizarAcciones();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrandoPedidos))]
    private PedidosSalonViewModel? _pedidosActual;

    public bool MostrandoPedidos => PedidosActual is not null;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrandoCocina))]
    [NotifyPropertyChangedFor(nameof(MostrandoModulo))]
    private CocinaViewModel? _cocinaActual;
    public bool MostrandoCocina => CocinaActual is not null;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrandoInventario))]
    [NotifyPropertyChangedFor(nameof(MostrandoModulo))]
    private InventarioLecturaViewModel? _inventarioActual;
    public bool MostrandoInventario => InventarioActual is not null;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrandoReservaciones))]
    [NotifyPropertyChangedFor(nameof(MostrandoModulo))]
    private ReservacionesViewModel? _reservacionesActual;
    public bool MostrandoReservaciones => ReservacionesActual is not null;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrandoReporteVentas))]
    [NotifyPropertyChangedFor(nameof(MostrandoModulo))]
    private ReporteVentasViewModel? _reporteVentasActual;
    public bool MostrandoReporteVentas => ReporteVentasActual is not null;
    public bool RolPuedeInventario => Rol is "Jefa" or "Supervisor" or "Personal de Cocina";
    public bool PuedeAbrirInventario => RolPuedeInventario && !_disposed && !MostrandoModulo && !Mutando && !ResultadoIncierto && !SeleccionandoGrupo && _coordinator.CanMutate;
    public bool RolPuedeReservaciones => Rol is "Jefa" or "Supervisor" or "Mesero" or "Encargado de Caja" or "Personal de Cocina";
    public bool PuedeAbrirReservaciones => RolPuedeReservaciones && !_disposed && !MostrandoModulo && !Mutando && !ResultadoIncierto && !SeleccionandoGrupo && _coordinator.CanMutate;
    public bool RolPuedeReporteVentas => Rol is "Jefa" or "Supervisor";
    public bool PuedeAbrirReporteVentas => RolPuedeReporteVentas && !_disposed && !MostrandoModulo && !Mutando && !ResultadoIncierto && !SeleccionandoGrupo && _coordinator.CanMutate;
    public bool MostrandoModulo => MostrandoPedidos || MostrandoCocina || MostrandoRecuperacionCaja ||
        MostrandoInventario || MostrandoReservaciones || MostrandoReporteVentas;
    public bool RolPuedeCocina => Rol is "Jefa" or "Supervisor" or "Personal de Cocina";
    public bool PuedeAbrirCocina => RolPuedeCocina && !_disposed && !MostrandoModulo && !Mutando && !ResultadoIncierto && !SeleccionandoGrupo && _coordinator.CanMutate;

    [RelayCommand]
    private async Task AbrirInventarioAsync()
    {
        if (!PuedeAbrirInventario) return;
        Mutando = true;
        bool reserved;
        try { reserved = await _coordinator.TryBeginMutationAsync(_lifetime.Token); }
        catch (OperationCanceledException) { Mutando = false; return; }
        if (!reserved) { Mutando = false; return; }
        InventarioActual = new InventarioLecturaViewModel(new InventarioLecturaService(_session), _session, Rol == "Jefa", CerrarInventario,
            (_session as SessionManager)?.InventarioEscrituras);
        ActualizarAcciones();
    }

    private void CerrarInventario()
    {
        InventarioActual?.Dispose();
        InventarioActual = null;
        _coordinator.CompleteMutation(requiresReconciliation: true);
        Mutando = false;
        SolicitarConsulta();
        ActualizarAcciones();
    }

    [RelayCommand]
    private async Task AbrirReservacionesAsync()
    {
        if (!PuedeAbrirReservaciones) return;
        Mutando = true;
        bool reserved;
        try { reserved = await _coordinator.TryBeginMutationAsync(_lifetime.Token); }
        catch (OperationCanceledException) { Mutando = false; return; }
        if (!reserved) { Mutando = false; return; }
        ReservacionesActual = new ReservacionesViewModel(_session, new ServerClock(), CerrarReservaciones,
            SolicitarConsulta);
        ActualizarAcciones();
    }

    private void CerrarReservaciones()
    {
        ReservacionesActual?.Dispose();
        ReservacionesActual = null;
        _coordinator.CompleteMutation(requiresReconciliation: true);
        Mutando = false;
        SolicitarConsulta();
        ActualizarAcciones();
    }

    [RelayCommand]
    private async Task AbrirReporteVentasAsync()
    {
        if (!PuedeAbrirReporteVentas) return;
        Mutando = true;
        bool reserved;
        try { reserved = await _coordinator.TryBeginMutationAsync(_lifetime.Token); }
        catch (OperationCanceledException) { Mutando = false; return; }
        if (!reserved) { Mutando = false; return; }
        ReporteVentasActual = new ReporteVentasViewModel(_session, new ServerClock(), CerrarReporteVentas);
        ActualizarAcciones();
    }

    private void CerrarReporteVentas()
    {
        ReporteVentasActual = null;
        _coordinator.CompleteMutation(requiresReconciliation: true);
        Mutando = false;
        SolicitarConsulta();
        ActualizarAcciones();
    }

    [RelayCommand]
    private async Task AbrirCocinaAsync()
    {
        if (!PuedeAbrirCocina) return;
        Mutando = true;
        bool reserved;
        try { reserved = await _coordinator.TryBeginMutationAsync(_lifetime.Token); }
        catch (OperationCanceledException) { Mutando = false; return; }
        if (!reserved) { Mutando = false; MensajeAccion = "No se abrió Cocina; revise la conexión."; return; }
        CocinaActual = new CocinaViewModel(_session, new CocinaApiService(_session), CerrarCocina);
        ActualizarAcciones();
    }

    private void CerrarCocina()
    {
        CocinaActual?.Dispose();
        CocinaActual = null;
        _coordinator.CompleteMutation(requiresReconciliation: true);
        Mutando = false;
        SolicitarConsulta();
        ActualizarAcciones();
    }

    partial void OnPedidosActualChanged(PedidosSalonViewModel? value) => OnPropertyChanged(nameof(MostrandoModulo));
    public bool PuedeAbrirCuenta => !_disposed && PedidosActual is null && !SeleccionandoGrupo && !Mutando &&
        !ResultadoIncierto && _coordinator.CanMutate && Rol is "Jefa" or "Supervisor" or "Mesero" or "Encargado de Caja" &&
        MesaSeleccionada is not null;

    [RelayCommand]
    private async Task AbrirCuentaAsync()
    {
        if (!PuedeAbrirCuenta || MesaSeleccionada is not { } mesa) return;
        Mutando = true;
        bool reserved;
        try { reserved = await _coordinator.TryBeginMutationAsync(_lifetime.Token); }
        catch (OperationCanceledException) { Mutando = false; return; }
        if (!reserved)
        {
            Mutando = false;
            MensajeAccion = "No se abrió la cuenta; revise la conexión y vuelva a intentarlo.";
            return;
        }
        var grupo = mesa.IdGrupo;
        PedidosActual = new PedidosSalonViewModel(grupo is null ? mesa.IdMesa : null, grupo,
            grupo is null ? $"Mesa {mesa.Numero:00}" : $"Grupo {grupo}",
            new ProductosPedidoApiService(_session), new CuentaPedidoApiService(_session), CerrarPedidos,
            new EnvioPedidoApiService(_session), grupo is null ? mesa.Version : null);
        ActualizarAcciones();
    }

    private void CerrarPedidos()
    {
        PedidosActual?.Dispose();
        PedidosActual = null;
        _coordinator.CompleteMutation(requiresReconciliation: true);
        Mutando = false;
        SolicitarConsulta();
        ActualizarAcciones();
    }

    [ObservableProperty] private string _mensajeAccion = "";
    [ObservableProperty] private bool _confirmarLiberacion;
    [ObservableProperty] private bool _resultadoIncierto;
    [ObservableProperty] private bool _consultaTrasIncertidumbre;
    [ObservableProperty] private bool _mutando;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnchoNavegacion))]
    [NotifyPropertyChangedFor(nameof(TextoNavegacion))]
    private bool _navegacionAbierta;

    public double AnchoNavegacion => NavegacionAbierta ? 200 : 0;
    public string TextoNavegacion => NavegacionAbierta ? "Ocultar navegación" : "Mostrar navegación";

    [RelayCommand] private void AlternarNavegacion() => NavegacionAbierta = !NavegacionAbierta;

    public bool PuedeReservar => PuedeCambiar("Reservada");
    public bool PuedeOcupar => PuedeCambiar("Ocupada");
    public bool PuedeLiberar => PuedeCambiar("Libre");
    public bool PuedeReconocer => ResultadoIncierto && ConsultaTrasIncertidumbre && _coordinator.CanAcknowledgeUncertainty;
    public string AyudaAcciones =>
        ResultadoIncierto ? "Revise las mesas y reconozca el aviso antes de realizar cambios." :
        _session.State == SessionLifecycleState.Paused ? "La sesión está en pausa; interactúe para continuar." :
        Mutando ? "Hay una operación en curso. Espere su resultado." :
        !RolPuedeModificar ? "Su rol permite consultar las mesas, pero no modificarlas." :
        !_coordinator.CanMutate ? "Los cambios estarán disponibles después de una consulta satisfactoria." :
        MesaSeleccionada?.IdGrupo is not null ? "Esta mesa pertenece a un grupo; los cambios se realizan cerrando el grupo." :
        MesaSeleccionada?.Estado switch
        {
            "Libre" => "Puede reservar u ocupar esta mesa.",
            "Reservada" => "Puede ocupar o liberar esta mesa.",
            "Ocupada" => "Puede solicitar la liberación; la API verificará si hay una cuenta pendiente.",
            _ => "Seleccione una mesa para consultar sus acciones."
        };

    private bool PuedeCambiar(string destino) => !_disposed && !Mutando && !ConfirmarLiberacion && !SeleccionandoGrupo &&
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
        OnPropertyChanged(nameof(AyudaAcciones));
        OnPropertyChanged(nameof(PuedeAbrirCuenta));
        OnPropertyChanged(nameof(PuedeAbrirCocina));
        OnPropertyChanged(nameof(PuedeAbrirRecuperacionCaja));
        OnPropertyChanged(nameof(PuedeAbrirInventario));
        OnPropertyChanged(nameof(PuedeAbrirReservaciones));
        OnPropertyChanged(nameof(PuedeAbrirReporteVentas));
        ActualizarAccionesGrupo();
    }

    partial void OnMesaSeleccionadaChanged(MesaDto? value) { ConfirmarLiberacion = false; ConfirmarCierreGrupo = false; ActualizarAcciones(); }
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
        if (!PuedeCambiar(destino) || MesaSeleccionada is not { } mesa) return;
        Mutando = true;
        bool iniciado;
        try { iniciado = await _coordinator.TryBeginMutationAsync(_lifetime.Token); }
        catch (OperationCanceledException) { Mutando = false; return; }
        if (!iniciado) { Mutando = false; MensajeAccion = "No se envió el cambio; revise la conexión y vuelva a intentarlo."; ActualizarAcciones(); return; }
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
                _ when (int)ex.StatusCode == 401 => "La autenticación no es válida. Inicie sesión nuevamente.",
                _ when (int)ex.StatusCode == 403 => "Su rol no tiene permiso para realizar esta acción.",
                _ => "La API rechazó el cambio. Revise la mesa actualizada antes de intentar otra acción."
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
    public int MesasLibres => Mesas.Count(m => m.Estado == "Libre");
    public int MesasOcupadas => Mesas.Count(m => m.Estado == "Ocupada");
    public int MesasReservadas => Mesas.Count(m => m.Estado == "Reservada");
    public int MesasAgrupadas => Mesas.Count(m => m.Estado == "Agrupada");

    private void AlCambiarMesas(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(MesasLibres));
        OnPropertyChanged(nameof(MesasOcupadas));
        OnPropertyChanged(nameof(MesasReservadas));
        OnPropertyChanged(nameof(MesasAgrupadas));
    }

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
        if (SeleccionandoGrupo) { AlternarMesaGrupo(mesa); return; }
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
        Func<Action, Task>? dispatch = null, IGruposMesaApiService? gruposService = null, CajaRecoveryViewModel? recoveryPanel = null)
    {
        Nombre = nombre;
        Rol = rol;
        _session = session;
        _recoveryPanel = recoveryPanel;
        _mesasService = mesasService;
        _gruposService = gruposService ?? new GruposMesaApiService(session);
        _dispatch = dispatch ?? DispatchToUiAsync;
        Mesas.CollectionChanged += AlCambiarMesas;
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
                            RevisarSeleccionGrupo();
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
                        EstadoConexion = (int)ex.StatusCode == 401
                            ? "La autenticación no es válida para consultar mesas."
                            : "Su rol no tiene permiso para consultar mesas.";
                        ConsultaTrasIncertidumbre = false;
                        ActualizarAcciones();
                    });
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or ApiException or System.Text.Json.JsonException or InvalidDataException)
                {
                    await _dispatch(() =>
                    {
                        if (_coordinator.CompleteRead(ticket.Value, false) ||
                            (Mutando && _session.State is SessionLifecycleState.Active or SessionLifecycleState.Expiring))
                        {
                            EstadoConexion = "Sin conexión o respuesta inválida. Mostrando últimos datos.";
                            ConsultaTrasIncertidumbre = false;
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
        _ = _dispatch(() =>
        {
            if (args.State == SessionLifecycleState.Paused)
                EstadoConexion = "Sesión en pausa. Interactúe para continuar.";
            else if (args.PreviousState == SessionLifecycleState.Paused &&
                     args.State is SessionLifecycleState.Active or SessionLifecycleState.Expiring)
                EstadoConexion = "Actualizando mesas…";
            if (args.State is not (SessionLifecycleState.Active or SessionLifecycleState.Expiring))
                ConsultaTrasIncertidumbre = false;
            ActualizarAcciones();
        });
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
        PedidosActual?.Dispose();
        CocinaActual?.Dispose();
        InventarioActual?.Dispose();
        ReservacionesActual?.Dispose();
        RecuperacionCajaActual?.Deactivate();
        _session.SessionStateChanged -= OnSessionStateChanged;
        Mesas.CollectionChanged -= AlCambiarMesas;
        _coordinator.Stop();
        _lifetime.Cancel();
    }
}
