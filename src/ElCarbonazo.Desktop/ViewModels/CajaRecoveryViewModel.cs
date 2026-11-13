using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;

namespace ElCarbonazo.Desktop.ViewModels;

// This screen is recovery-only: it never starts or executes a cash operation.
public partial class CajaRecoveryViewModel : ViewModelBase, ICajaRecoverySnapshotSink
{
    private readonly CajaRecoveryStore? _store;
    private readonly ISessionManager? _session;
    private readonly Func<long>? _generation;
    private CajaRecoveryCoordinator? _coordinator;
    private Action? _back;
    private long _openedGeneration;
    private int _openedActor;
    private bool _open;

    [ObservableProperty] private string _estadoDiario = "Diario no disponible.";
    [ObservableProperty] private string _mensaje = "Consulte la operación antes de tomar una decisión.";
    [ObservableProperty] private string _fotografia = "Sin fotografía reconciliada.";
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private bool _confirmarCancelacion;

    public CajaRecoveryViewModel() { }
    public CajaRecoveryViewModel(CajaRecoveryStore store, ISessionManager session, Func<long> generation)
    {
        _store = store;
        _session = session;
        _generation = generation;
    }

    public void Attach(CajaRecoveryCoordinator coordinator) => _coordinator = coordinator;

    public bool Open(Action back)
    {
        if (_session?.CurrentSession is not { } session || session.Rol is not ("Jefa" or "Encargado de Caja") || _generation is null) return false;
        _openedActor = session.IdUsuario;
        _openedGeneration = _generation();
        _open = true;
        _back = back;
        Fotografia = "Sin fotografía reconciliada.";
        Mensaje = "Consulte la operación antes de tomar una decisión. No se reintentará ningún cobro automáticamente.";
        ConfirmarCancelacion = false;
        ActualizarDiario();
        return true;
    }

    public void Deactivate()
    {
        _open = false;
        _back = null;
        Fotografia = "Sin fotografía reconciliada.";
    }

    private bool Vigente() => _open && _session?.CurrentSession is { } session && session.IdUsuario == _openedActor &&
        (session.Rol is "Jefa" or "Encargado de Caja") && _generation?.Invoke() == _openedGeneration &&
        (_session.State is SessionLifecycleState.Active or SessionLifecycleState.Expiring);

    private void ActualizarDiario()
    {
        var state = _store?.Read();
        EstadoDiario = state switch
        {
            null => "Diario no disponible o no protegido. No inicie operaciones de caja.",
            { Estado: "SinPendientes" } => "No hay operaciones locales pendientes.",
            _ when !Vigente() => "La sesión no permite consultar la operación.",
            _ when state.Propietario != _openedActor && _session?.CurrentSession?.Rol != "Jefa" => "Hay una operación de otra persona; solo Jefa puede supervisarla.",
            _ => $"{state.Estado} · {state.Tipo} · operación {state.Operacion:D} · propietario {state.Propietario}"
        };
    }

    [RelayCommand]
    private async Task ConsultarAsync(CancellationToken cancellationToken)
    {
        if (Ocupado || !Vigente() || _coordinator is null) return;
        Ocupado = true;
        try
        {
            var result = await _coordinator.RecoverAsync(cancellationToken);
            Mensaje = result switch
            {
                CajaRecoveryOutcome.Prepared => "Preparada: no demuestra que la ejecución haya terminado. No se ejecutará automáticamente.",
                CajaRecoveryOutcome.NotFound => "No encontrada: no demuestra que una solicitud tardía no llegue. Considere cancelar para establecer una barrera.",
                CajaRecoveryOutcome.Completed => "Completada. Concilie la fotografía antes de continuar.",
                CajaRecoveryOutcome.Rejected => "Rechazada. Concilie la fotografía antes de continuar.",
                CajaRecoveryOutcome.Cancelled => "Cancelada. Concilie la fotografía antes de continuar.",
                _ => "No se pudo determinar el resultado. El diario permanece bloqueado."
            };
        }
        finally { Ocupado = false; ActualizarDiario(); }
    }

    [RelayCommand]
    private async Task CancelarAsync(CancellationToken cancellationToken)
    {
        if (Ocupado || !Vigente() || !ConfirmarCancelacion || _coordinator is null) return;
        ConfirmarCancelacion = false;
        Ocupado = true;
        try
        {
            var result = await _coordinator.CancelAsync(cancellationToken, confirmaIntervencion: true);
            Mensaje = result switch
            {
                CajaRecoveryOutcome.Cancelled => "Cancelada. Concilie antes de continuar.",
                CajaRecoveryOutcome.Completed => "La operación ya se completó; la cancelación no revierte su efecto. Concilie.",
                CajaRecoveryOutcome.Rejected => "La operación ya fue rechazada. Concilie.",
                _ => "La cancelación no pudo confirmarse. Consulte de nuevo; no repita el pago externo."
            };
        }
        finally { Ocupado = false; ActualizarDiario(); }
    }

    [RelayCommand]
    private async Task ConciliarAsync(CancellationToken cancellationToken)
    {
        if (Ocupado || !Vigente() || _coordinator is null) return;
        Ocupado = true;
        try
        {
            Mensaje = await _coordinator.ReconcileAsync(cancellationToken)
                ? "Fotografía aplicada. Revise los datos antes de iniciar una nueva operación."
                : "No se pudo conciliar. El diario permanece bloqueado; revise la sesión y la conexión.";
        }
        finally { Ocupado = false; ActualizarDiario(); }
    }

    [RelayCommand]
    private void Volver()
    {
        if (Ocupado) return;
        var back = _back;
        Deactivate();
        back?.Invoke();
    }

    public Task<bool> ApplyShiftAsync(CajaTurnoSnapshot snapshot, CancellationToken ct) => Apply($"Turno {snapshot.IdTurnoCaja}: {snapshot.Estado}; efectivo esperado C$ {snapshot.EfectivoEsperado:N2}; cuentas pendientes {snapshot.CantidadCuentasPendientes}.", ct);
    public Task<bool> ApplyPaymentAsync(CajaPagoSnapshot snapshot, CancellationToken ct) => Apply($"Comprobante interno {snapshot.NumeroComprobante} — DEMOSTRACIÓN, NO ES FACTURA FISCAL. Cuenta {snapshot.IdPedido}, total C$ {snapshot.Total:N2}, método {snapshot.MetodoPago}.", ct);
    public Task<bool> ApplyCurrentShiftAsync(CajaTurnoSnapshot? snapshot, CancellationToken ct) => Apply(snapshot is null ? "No hay turno abierto." : $"Turno actual {snapshot.IdTurnoCaja}: {snapshot.Estado}; efectivo esperado C$ {snapshot.EfectivoEsperado:N2}.", ct);
    public Task<bool> ApplyAccountAndShiftAsync(CajaCuentaRecoverySnapshot account, CajaTurnoSnapshot shift, CancellationToken ct) => Apply($"Cuenta {account.IdPedido}: {account.Estado}, total C$ {account.Total:N2}, {(account.Cobrable ? "cobrable" : account.Motivo)}. Turno {shift.IdTurnoCaja}: {shift.Estado}. Revise el detalle completo antes de cobrar.", ct);
    public Task<bool> ApplyBarrierAsync(string attemptedType, CajaTurnoSnapshot? currentShift, CancellationToken ct) => Apply($"Barrera sin intención ({attemptedType}). {(currentShift is null ? "No hay turno abierto." : $"Turno actual {currentShift.IdTurnoCaja}: {currentShift.Estado}.")} Revise nuevamente cuentas y comprobantes antes de cualquier operación.", ct);

    private Task<bool> Apply(string value, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !Vigente() || !Ocupado) return Task.FromResult(false);
        // This recovery-only screen has no account or receipt cache or write controls.
        Fotografia = value;
        return Task.FromResult(true);
    }
}
