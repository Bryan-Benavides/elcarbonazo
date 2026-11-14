using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.Text;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;

namespace ElCarbonazo.Desktop.ViewModels;

// Opening uses the durable coordinator; recovery never executes a previously prepared intention.
public partial class CajaRecoveryViewModel : ViewModelBase, ICajaRecoverySnapshotSink, ICajaRejectionSnapshotSink, ICajaHistorySnapshotSink
{
    private readonly CajaRecoveryStore? _store;
    private readonly ISessionManager? _session;
    private readonly Func<long>? _generation;
    private CajaRecoveryCoordinator? _coordinator;
    private ICajaTurnoActualReader? _shiftReader;
    private ICajaCuentasReader? _accountsReader;
    private ICajaComprobantesReader? _receiptsReader;
    private string? _nextReceiptsCursor;
    private CajaTurnoSnapshot? _turnoActual;
    private string? _nextAccountsCursor;
    private Action? _back;
    private long _openedGeneration;
    private int _openedActor;
    private bool _open;
    private bool _referenceCollisionApplied;
    private sealed record ExternalAttempt(Guid Operation, int Account, int Shift, string Method, string Reference, long Generation);
    private ExternalAttempt? _externalAttempt;
    private ExternalAttempt? _repeatedReference;
    private bool _reviewedShiftAfterCollision;
    private bool _reviewedAccountsAfterCollision;

    [ObservableProperty] private string _estadoDiario = "Diario no disponible.";
    [ObservableProperty] private string _mensaje = "Consulte la operación antes de tomar una decisión.";
    [ObservableProperty] private string _fotografia = "Sin fotografía reconciliada.";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeAbrirTurno))]
    private bool _ocupado;
    [ObservableProperty] private bool _confirmarCancelacion;
    [ObservableProperty] private string _resumenTurno = "Consulte el turno antes de abrir caja.";
    [ObservableProperty] private string _fondoInicial = "0.00";
    [ObservableProperty] private string _estadoCuentas = "Consulte las cuentas para ver sus estados.";
    [ObservableProperty] private bool _hayMasCuentas;
    [ObservableProperty] private bool _hayMasComprobantes;
    [ObservableProperty] private string _estadoComprobantes = "Consulte los comprobantes internos.";
    public ObservableCollection<CajaComprobanteResumen> Comprobantes { get; } = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayCuentaSeleccionada))]
    [NotifyPropertyChangedFor(nameof(CambioPrevisto))]
    private CajaCuentaVista? _cuentaSeleccionada;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CambioPrevisto))]
    private string _efectivoRecibido = "";
    [ObservableProperty] private bool _confirmarCobro;
    [ObservableProperty] private bool _confirmarVerificacionExterna;
    [ObservableProperty] private bool _confirmarReferenciaRepetida;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEfectivo))]
    [NotifyPropertyChangedFor(nameof(EsPagoExterno))]
    private string _metodoPago = "Efectivo";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RevisionReferencia))]
    private string _referenciaPago = "";
    public IReadOnlyList<string> MetodosPago { get; } = ["Efectivo", "Tarjeta", "Transferencia"];
    public bool EsEfectivo => MetodoPago == "Efectivo";
    public bool EsPagoExterno => MetodoPago is "Tarjeta" or "Transferencia";
    public string RevisionReferencia
    {
        get
        {
            try
            {
                var reference = ReferenciaPago.Normalize(NormalizationForm.FormC).Trim();
                return reference.Length > 0 && reference.EnumerateRunes().Count() <= 100 && !reference.EnumerateRunes().Any(Rune.IsControl)
                    ? $"Referencia para revisión: {reference}"
                    : "Introduzca una referencia de 1 a 100 caracteres Unicode, sin controles.";
            }
            catch (ArgumentException) { return "La referencia contiene caracteres Unicode inválidos."; }
        }
    }
    partial void OnMetodoPagoChanged(string value)
    {
        ConfirmarCobro = false;
        ConfirmarVerificacionExterna = false;
        ConfirmarReferenciaRepetida = false;
        EfectivoRecibido = "";
        ReferenciaPago = "";
    }
    partial void OnReferenciaPagoChanged(string value)
    {
        ConfirmarCobro = false;
        ConfirmarVerificacionExterna = false;
        ConfirmarReferenciaRepetida = false;
    }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiferenciaCierre))]
    private string _efectivoContado = "";
    [ObservableProperty] private string _motivoCierre = "";
    [ObservableProperty] private bool _confirmarCierreCaja;
    public string DiferenciaCierre => _turnoActual is { Estado: "Abierto" } turno && TryMoney(EfectivoContado, out var counted)
        ? DescribirDiferencia(turno, counted)
        : "Consulte el turno y escriba el conteo para revisar la diferencia.";
    partial void OnEfectivoContadoChanged(string value) => ConfirmarCierreCaja = false;
    partial void OnMotivoCierreChanged(string value) => ConfirmarCierreCaja = false;
    public bool HayCuentaSeleccionada => CuentaSeleccionada is not null;
    public string CambioPrevisto => CuentaSeleccionada is { } cuenta &&
        decimal.TryParse(EfectivoRecibido, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var cash) &&
        cash >= cuenta.Total && cash <= 9999999999.99m && decimal.Round(cash, 2) == cash
        ? $"Vuelto previsto: C$ {cash - cuenta.Total:N2}. La API confirmará el cálculo."
        : "Introduzca efectivo suficiente para calcular el vuelto previsto.";
    partial void OnEfectivoRecibidoChanged(string value) => ConfirmarCobro = false;
    public ObservableCollection<CajaCuentaVista> Cuentas { get; } = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeAbrirTurno))]
    private bool _confirmarApertura;
    private bool _turnoConsultadoSinAbierto;
    public bool PuedeAbrirTurno => !Ocupado && _turnoConsultadoSinAbierto && ConfirmarApertura;
    partial void OnFondoInicialChanged(string value) => ConfirmarApertura = false;

    public CajaRecoveryViewModel() { }
    public CajaRecoveryViewModel(CajaRecoveryStore store, ISessionManager session, Func<long> generation)
    {
        _store = store;
        _session = session;
        _generation = generation;
    }

    public void Attach(CajaRecoveryCoordinator coordinator) => _coordinator = coordinator;
    public void AttachShiftReader(ICajaTurnoActualReader reader) => _shiftReader = reader;
    public void AttachAccountsReader(ICajaCuentasReader reader) => _accountsReader = reader;
    public void AttachReceiptsReader(ICajaComprobantesReader reader) => _receiptsReader = reader;

    public bool Open(Action back)
    {
        if (Ocupado || _session?.CurrentSession is not { } session || session.Rol is not ("Jefa" or "Encargado de Caja") || _generation is null) return false;
        _openedActor = session.IdUsuario;
        _openedGeneration = _generation();
        _open = true;
        _externalAttempt = null;
        _repeatedReference = null;
        Comprobantes.Clear();
        _nextReceiptsCursor = null;
        HayMasComprobantes = false;
        _back = back;
        Fotografia = "Sin fotografía reconciliada.";
        Mensaje = "Consulte la operación antes de tomar una decisión. No se reintentará ningún cobro automáticamente.";
        ConfirmarCancelacion = false;
        ConfirmarApertura = false;
        _turnoConsultadoSinAbierto = false;
        _turnoActual = null;
        EfectivoContado = "";
        MotivoCierre = "";
        ConfirmarCierreCaja = false;
        OnPropertyChanged(nameof(DiferenciaCierre));
        ResumenTurno = "Consulte el turno antes de abrir caja.";
        InvalidarCuentas();
        ActualizarDiario();
        return true;
    }

    public void Deactivate()
    {
        _open = false;
        Comprobantes.Clear();
        _nextReceiptsCursor = null;
        HayMasComprobantes = false;
        _externalAttempt = null;
        _repeatedReference = null;
        _back = null;
        _turnoConsultadoSinAbierto = false;
        _turnoActual = null;
        Fotografia = "Sin fotografía reconciliada.";
        InvalidarCuentas();
    }

    [RelayCommand]
    private Task ConsultarComprobantesAsync(CancellationToken ct) => LoadReceiptsAsync(null, ct);

    [RelayCommand]
    private Task SiguientesComprobantesAsync(CancellationToken ct) => _nextReceiptsCursor is { } cursor
        ? LoadReceiptsAsync(cursor, ct) : Task.CompletedTask;

    private async Task LoadReceiptsAsync(string? cursor, CancellationToken ct)
    {
        if (Ocupado || !Vigente() || _receiptsReader is null) return;
        Comprobantes.Clear();
        _nextReceiptsCursor = null;
        HayMasComprobantes = false;
        Ocupado = true;
        try
        {
            var page = await _receiptsReader.ReadPageAsync(_openedActor, cursor, ct);
            if (!Vigente() || page is null) { EstadoComprobantes = "No se pudieron consultar los comprobantes."; return; }
            foreach (var item in page.Items) Comprobantes.Add(item);
            _nextReceiptsCursor = page.CursorSiguiente;
            HayMasComprobantes = _nextReceiptsCursor is not null;
            EstadoComprobantes = Comprobantes.Count == 0 ? "No hay comprobantes en esta página." : "Seleccione un comprobante para consultar su detalle.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException or SessionExpiredException or SessionPausedException or SecurityConnectionException)
        { EstadoComprobantes = "No se pudieron consultar los comprobantes."; }
        finally { Ocupado = false; }
    }

    [RelayCommand]
    private async Task VerComprobanteAsync(CajaComprobanteResumen? item, CancellationToken ct)
    {
        if (Ocupado || !Vigente() || item is null || !Comprobantes.Contains(item) || _receiptsReader is null) return;
        Ocupado = true;
        try
        {
            EstadoComprobantes = await _receiptsReader.ApplyDetailAsync(_openedActor, item.IdCobro, ct) && Vigente()
                ? "Detalle validado en fotografía reconciliada." : "No se pudo validar el detalle del comprobante.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException or SessionExpiredException or SessionPausedException or SecurityConnectionException)
        { EstadoComprobantes = "No se pudo consultar el detalle del comprobante."; }
        finally { Ocupado = false; }
    }

    private void InvalidarCuentas()
    {
        CancelarSeleccionCobro();
        Cuentas.Clear();
        _nextAccountsCursor = null;
        HayMasCuentas = false;
        EstadoCuentas = "Consulte las cuentas para ver sus estados.";
    }

    [RelayCommand]
    private void SeleccionarCuenta(CajaCuentaVista? account)
    {
        if (Ocupado || !Vigente() || account is null || !Cuentas.Contains(account) || !account.Cobrable ||
            account.Estado != "Listo" || account.Total <= 0 || account.HuellaLineas is null || _turnoActual?.Estado != "Abierto" ||
            _store?.Read()?.Estado != "SinPendientes")
        {
            Mensaje = "Consulte el turno abierto y una cuenta cobrable; resuelva primero cualquier operación pendiente.";
            return;
        }
        CuentaSeleccionada = account;
        MetodoPago = "Efectivo";
        ReferenciaPago = "";
        EfectivoRecibido = "";
        ConfirmarCobro = false;
        ConfirmarVerificacionExterna = false;
        ConfirmarReferenciaRepetida = false;
    }

    [RelayCommand]
    private async Task CobrarExternoAsync(CancellationToken cancellationToken)
    {
        var account = CuentaSeleccionada;
        var shift = _turnoActual;
        if (Ocupado || !Vigente() || _coordinator is null || !EsPagoExterno || !ConfirmarCobro || !ConfirmarVerificacionExterna ||
            account is null || !Cuentas.Contains(account) || !account.Cobrable || account.Estado != "Listo" || account.Total <= 0 ||
            account.HuellaLineas is null || shift?.Estado != "Abierto" || _store?.Read()?.Estado != "SinPendientes") return;
        string reference;
        try { reference = ReferenciaPago.Normalize(NormalizationForm.FormC).Trim(); }
        catch (ArgumentException) { reference = ""; }
        if (string.IsNullOrWhiteSpace(reference) || reference.EnumerateRunes().Count() > 100 || reference.EnumerateRunes().Any(Rune.IsControl))
        {
            ConfirmarCobro = false;
            ConfirmarVerificacionExterna = false;
            Mensaje = "Introduzca una referencia válida de 1 a 100 caracteres, sin controles.";
            return;
        }
        var collision = _repeatedReference;
        var repeated = collision is not null && collision.Account == account.IdPedido && collision.Shift == shift.IdTurnoCaja &&
            collision.Method == MetodoPago && collision.Reference == reference && collision.Generation == _openedGeneration;
        if (repeated && (!_reviewedShiftAfterCollision || !_reviewedAccountsAfterCollision || !ConfirmarReferenciaRepetida))
        {
            Mensaje = "Consulte nuevamente el turno y la cuenta y confirme expresamente la coincidencia.";
            return;
        }
        var operation = Guid.NewGuid();
        var attempt = new ExternalAttempt(operation, account.IdPedido, shift.IdTurnoCaja, MetodoPago, reference, _openedGeneration);
        var arguments = new
        {
            pedidoId = account.IdPedido,
            turnoId = shift.IdTurnoCaja,
            totalVisto = account.Total,
            huellaLineas = account.HuellaLineas,
            metodo = MetodoPago,
            efectivoRecibido = (decimal?)null,
            referencia = reference,
            verificacionExterna = true,
            referenciaRepetida = repeated
        };
        _repeatedReference = null;
        _externalAttempt = attempt;
        InvalidarCuentas();
        InvalidarTurno();
        Ocupado = true;
        try
        {
            var result = await _coordinator.BeginAsync(operation, _openedActor, "CobrarCuenta", cancellationToken, arguments);
            if (result == CajaRecoveryOutcome.Prepared && Vigente()) result = await _coordinator.ExecuteAsync(cancellationToken);
            if (result == CajaRecoveryOutcome.Completed && Vigente() && await _coordinator.ReconcileAsync(cancellationToken))
            {
                _externalAttempt = null;
                Mensaje = "Cobro registrado y comprobante reconciliado. La mesa o grupo no se libera automáticamente.";
            }
            else Mensaje = "El registro no está conciliado. El pago externo pudo ocurrir; no lo repita. Consulte el diario.";
        }
        catch (OperationCanceledException) { Mensaje = "El registro pudo quedar pendiente. No repita el pago externo; consulte el diario."; }
        finally { Ocupado = false; ActualizarDiario(); }
    }

    [RelayCommand]
    private void CancelarSeleccionCobro()
    {
        CuentaSeleccionada = null;
        ConfirmarCobro = false;
        ConfirmarVerificacionExterna = false;
        ConfirmarReferenciaRepetida = false;
        EfectivoRecibido = "";
        ReferenciaPago = "";
        MetodoPago = "Efectivo";
    }

    [RelayCommand]
    private async Task CobrarEfectivoAsync(CancellationToken cancellationToken)
    {
        var account = CuentaSeleccionada;
        var shift = _turnoActual;
        if (Ocupado || !Vigente() || _coordinator is null || !EsEfectivo || !ConfirmarCobro || account is null ||
            !Cuentas.Contains(account) || !account.Cobrable || account.Estado != "Listo" || account.Total <= 0 ||
            account.HuellaLineas is null || shift?.Estado != "Abierto" || _store?.Read()?.Estado != "SinPendientes") return;
        if (!decimal.TryParse(EfectivoRecibido, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var cash) ||
            cash < account.Total || cash > 9999999999.99m || decimal.Round(cash, 2) != cash)
        {
            ConfirmarCobro = false;
            Mensaje = "El efectivo debe cubrir el total y tener como máximo dos decimales, usando punto decimal.";
            return;
        }
        var arguments = new
        {
            pedidoId = account.IdPedido,
            turnoId = shift.IdTurnoCaja,
            totalVisto = account.Total,
            huellaLineas = account.HuellaLineas,
            metodo = "Efectivo",
            efectivoRecibido = (decimal?)cash,
            referencia = (string?)null,
            verificacionExterna = false,
            referenciaRepetida = false
        };
        InvalidarCuentas();
        _turnoActual = null;
        ResumenTurno = "Consulte nuevamente el turno después de resolver el cobro.";
        Ocupado = true;
        Mensaje = "Registrando cobro. No repita la operación.";
        try
        {
            var result = await _coordinator.BeginAsync(Guid.NewGuid(), _openedActor, "CobrarCuenta", cancellationToken, arguments);
            if (result == CajaRecoveryOutcome.Prepared && Vigente()) result = await _coordinator.ExecuteAsync(cancellationToken);
            if (result == CajaRecoveryOutcome.Completed && Vigente() && await _coordinator.ReconcileAsync(cancellationToken))
                Mensaje = "Cobro confirmado y comprobante reconciliado. La mesa o grupo no se libera automáticamente.";
            else
                Mensaje = "El cobro no se confirmó y concilió. Consulte el diario y no repita la operación.";
        }
        catch (OperationCanceledException)
        {
            Mensaje = "El cobro pudo quedar pendiente. Consulte el diario y no repita la operación.";
        }
        finally { Ocupado = false; ActualizarDiario(); }
    }

    [RelayCommand]
    private Task ConsultarCuentasAsync(CancellationToken cancellationToken) => CargarCuentasAsync(null, cancellationToken);

    [RelayCommand]
    private Task SiguientesCuentasAsync(CancellationToken cancellationToken) =>
        _nextAccountsCursor is { } cursor ? CargarCuentasAsync(cursor, cancellationToken) : Task.CompletedTask;

    private async Task CargarCuentasAsync(string? cursor, CancellationToken ct)
    {
        if (Ocupado || !Vigente() || _accountsReader is null) return;
        var generation = _openedGeneration;
        var actor = _openedActor;
        Ocupado = true;
        // Discard old results before reading; stale pages must never become a basis for a future payment.
        InvalidarCuentas();
        try
        {
            var page = await _accountsReader.ReadAsync(actor, cursor, ct);
            if (!Vigente() || _openedGeneration != generation || _openedActor != actor || page is null)
            {
                EstadoCuentas = "No se pudieron consultar las cuentas. Vuelva a consultar.";
                return;
            }
            foreach (var account in page.Items) Cuentas.Add(account);
            if (_repeatedReference is not null) _reviewedAccountsAfterCollision = true;
            _nextAccountsCursor = page.CursorSiguiente;
            HayMasCuentas = _nextAccountsCursor is not null;
            EstadoCuentas = Cuentas.Count == 0 ? "No hay cuentas en esta página." : "Información de consulta; la API vuelve a validar antes de cualquier cobro.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException or SessionExpiredException or SessionPausedException or SecurityConnectionException)
        {
            EstadoCuentas = "No se pudieron consultar las cuentas. Vuelva a consultar.";
        }
        finally { Ocupado = false; }
    }

    [RelayCommand]
    private async Task ConsultarTurnoAsync(CancellationToken cancellationToken)
    {
        if (Ocupado || !Vigente() || _shiftReader is null) return;
        Ocupado = true;
        _turnoConsultadoSinAbierto = false;
        _turnoActual = null;
        CancelarSeleccionCobro();
        ConfirmarApertura = false;
        ConfirmarCierreCaja = false;
        try
        {
            var (valid, shift) = await _shiftReader.ReadCurrentShiftAsync(_openedActor, cancellationToken);
            if (!Vigente() || !valid)
            {
                ResumenTurno = "No se pudo consultar el turno. No se habilita la apertura.";
                return;
            }
            _turnoConsultadoSinAbierto = shift is null;
            _turnoActual = shift;
            if (_repeatedReference is not null && shift?.IdTurnoCaja == _repeatedReference.Shift) _reviewedShiftAfterCollision = true;
            ResumenTurno = shift is null ? "No hay turno abierto." : DescribeTurno(shift);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException or SessionExpiredException or SessionPausedException or SecurityConnectionException)
        {
            ResumenTurno = "No se pudo consultar el turno. No se habilita la apertura.";
        }
        finally { Ocupado = false; ActualizarDiario(); OnPropertyChanged(nameof(DiferenciaCierre)); }
    }

    [RelayCommand]
    private async Task AbrirTurnoAsync(CancellationToken cancellationToken)
    {
        if (Ocupado || !Vigente() || _coordinator is null || !_turnoConsultadoSinAbierto || !ConfirmarApertura) return;
        if (_store?.Read()?.Estado != "SinPendientes")
        {
            ConfirmarApertura = false;
            Mensaje = "El diario no está disponible o tiene una operación pendiente. Resuelva la recuperación antes de abrir caja.";
            ActualizarDiario();
            return;
        }
        if (!decimal.TryParse(FondoInicial, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fund) ||
            fund < 0 || fund > 9999999999.99m || decimal.Round(fund, 2) != fund)
        {
            Mensaje = "Introduzca un fondo no negativo con hasta dos decimales, usando punto decimal.";
            ConfirmarApertura = false;
            return;
        }
        ConfirmarApertura = false;
        _turnoConsultadoSinAbierto = false;
        InvalidarCuentas();
        _turnoActual = null;
        Ocupado = true;
        ResumenTurno = "Apertura en curso. No repita la operación.";
        try
        {
            var outcome = await _coordinator.BeginAsync(Guid.NewGuid(), _openedActor, "AbrirTurno", cancellationToken,
                new { fondoInicial = fund, confirmado = true });
            if (outcome == CajaRecoveryOutcome.Prepared && Vigente())
                outcome = await _coordinator.ExecuteAsync(cancellationToken);
            if (outcome == CajaRecoveryOutcome.Completed && Vigente() && await _coordinator.ReconcileAsync(cancellationToken))
                Mensaje = "Turno abierto y reconciliado. Revise el resumen.";
            else
                Mensaje = "No se confirmó y concilió la apertura. Consulte el diario; no repita la operación.";
        }
        catch (OperationCanceledException)
        {
            Mensaje = "La apertura pudo quedar pendiente. Consulte el diario; no repita la operación.";
        }
        finally { Ocupado = false; ActualizarDiario(); }
    }

    private static string DescribeTurno(CajaTurnoSnapshot shift) =>
        $"Turno {shift.IdTurnoCaja}: {shift.Estado}. Abierto por usuario {shift.IdUsuarioApertura}. Fondo C$ {shift.FondoInicial:N2}; efectivo C$ {shift.TotalEfectivo:N2}; tarjeta C$ {shift.TotalTarjeta:N2}; transferencia C$ {shift.TotalTransferencia:N2}; efectivo esperado C$ {shift.EfectivoEsperado:N2}; cuentas pendientes {shift.CantidadCuentasPendientes}.";

    private static bool TryMoney(string value, out decimal amount) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) &&
        amount >= 0 && amount <= 9999999999.99m && decimal.Round(amount, 2) == amount;

    private static string DescribirDiferencia(CajaTurnoSnapshot shift, decimal counted)
    {
        var difference = counted - shift.EfectivoEsperado;
        var description = difference > 0 ? "Sobrante" : difference < 0 ? "Faltante" : "Sin diferencia";
        return $"{description}: C$ {difference:N2}. Cuentas pendientes: {shift.CantidadCuentasPendientes}; permanecerán sin pagar.";
    }

    [RelayCommand]
    private async Task CerrarTurnoAsync(CancellationToken cancellationToken)
    {
        var shift = _turnoActual;
        if (Ocupado || !Vigente() || _coordinator is null || shift?.Estado != "Abierto" || !ConfirmarCierreCaja) return;
        if (_store?.Read()?.Estado != "SinPendientes")
        {
            ConfirmarCierreCaja = false;
            Mensaje = "Resuelva primero la operación pendiente del diario antes de cerrar el turno.";
            return;
        }
        if (!TryMoney(EfectivoContado, out var counted))
        {
            ConfirmarCierreCaja = false;
            Mensaje = "Introduzca un conteo no negativo con hasta dos decimales, usando punto decimal.";
            return;
        }
        string? reason = null;
        if (counted != shift.EfectivoEsperado)
        {
            try { reason = MotivoCierre.Normalize(NormalizationForm.FormC).Trim(); }
            catch (ArgumentException) { reason = null; }
            if (string.IsNullOrWhiteSpace(reason) || reason.EnumerateRunes().Count() > 500 || reason.EnumerateRunes().Any(Rune.IsControl))
            {
                ConfirmarCierreCaja = false;
                Mensaje = "Explique la diferencia con un motivo válido de hasta 500 caracteres.";
                return;
            }
        }
        else if (!string.IsNullOrWhiteSpace(MotivoCierre))
        {
            ConfirmarCierreCaja = false;
            Mensaje = "Si no hay diferencia, quite el motivo antes de confirmar.";
            return;
        }
        ConfirmarCierreCaja = false;
        InvalidarCuentas();
        InvalidarTurno();
        Ocupado = true;
        Mensaje = "Cierre en curso. No repita la operación.";
        try
        {
            var outcome = await _coordinator.BeginAsync(Guid.NewGuid(), _openedActor, "CerrarTurno", cancellationToken,
                new { turnoId = shift.IdTurnoCaja, efectivoContado = counted, motivo = reason, confirmado = true });
            if (outcome == CajaRecoveryOutcome.Prepared && Vigente()) outcome = await _coordinator.ExecuteAsync(cancellationToken);
            if (outcome == CajaRecoveryOutcome.Completed && Vigente() && await _coordinator.ReconcileAsync(cancellationToken))
                Mensaje = "Turno cerrado y reconciliado. Las cuentas pendientes siguen sin pagar.";
            else
                Mensaje = "No se confirmó y concilió el cierre. Consulte el diario; no repita la operación.";
        }
        catch (OperationCanceledException)
        {
            Mensaje = "El cierre pudo quedar pendiente. Consulte el diario; no repita la operación.";
        }
        finally { Ocupado = false; ActualizarDiario(); }
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
        InvalidarCuentas();
        InvalidarTurno();
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
        InvalidarCuentas();
        InvalidarTurno();
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
        InvalidarCuentas();
        InvalidarTurno();
        Ocupado = true;
        _referenceCollisionApplied = false;
        try
        {
            var reconciled = await _coordinator.ReconcileAsync(cancellationToken);
            if (reconciled && _referenceCollisionApplied && _externalAttempt is { } attempt && Vigente())
            {
                _repeatedReference = attempt;
                _reviewedAccountsAfterCollision = false;
                _reviewedShiftAfterCollision = false;
            }
            if (reconciled) _externalAttempt = null;
            Mensaje = reconciled
                ? _referenceCollisionApplied
                    ? "La referencia coincide con otra del mismo método. No implica fraude. Consulte nuevamente el turno y la cuenta y revise antes de confirmar."
                    : "Fotografía aplicada. Revise los datos antes de iniciar una nueva operación."
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

    private void InvalidarTurno()
    {
        _turnoActual = null;
        _turnoConsultadoSinAbierto = false;
        ConfirmarApertura = false;
        ConfirmarCierreCaja = false;
        ResumenTurno = "Consulte nuevamente el turno antes de operar.";
        OnPropertyChanged(nameof(DiferenciaCierre));
    }

    public Task<bool> ApplyShiftAsync(CajaTurnoSnapshot snapshot, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !Vigente() || !Ocupado) return Task.FromResult(false);
        ResumenTurno = DescribeTurno(snapshot);
        _turnoActual = snapshot.Estado == "Abierto" ? snapshot : null;
        OnPropertyChanged(nameof(DiferenciaCierre));
        return Apply(ResumenTurno, ct);
    }
    public Task<bool> ApplyPaymentAsync(CajaPagoSnapshot snapshot, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !Vigente() || !Ocupado) return Task.FromResult(false);
        InvalidarCuentas();
        _turnoActual = null;
        return Apply(DescribePayment(snapshot), ct);
    }

    public Task<bool> ApplyHistoryPaymentAsync(CajaPagoSnapshot snapshot, CancellationToken ct) => Apply(DescribePayment(snapshot), ct);

    private static string DescribePayment(CajaPagoSnapshot snapshot)
    {
        var cash = snapshot.MetodoPago == "Efectivo" ? $" Recibido C$ {snapshot.EfectivoRecibido:N2}; vuelto C$ {snapshot.Vuelto:N2}." : "";
        var lines = string.Join(Environment.NewLine, snapshot.Lineas.Select(line =>
            $"{line.Cantidad} × {line.NombreProducto} · C$ {line.PrecioUnitario:N2} · subtotal C$ {line.Subtotal:N2}"));
        var date = snapshot.FechaHora is { } instant
            ? TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById("America/Managua")).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)
            : "Fecha no disponible";
        var reference = snapshot.ReferenciaExterna is null ? "" : $" Referencia: {snapshot.ReferenciaExterna}.";
        return $"Comprobante interno {snapshot.NumeroComprobante} — DEMOSTRACIÓN, NO ES FACTURA FISCAL.{Environment.NewLine}" +
            $"{date} · {snapshot.Destino ?? "Destino no disponible"} · cuenta {snapshot.IdPedido} · turno {snapshot.IdTurnoCaja} · actor {snapshot.IdUsuario}{Environment.NewLine}" +
            $"{lines}{Environment.NewLine}Subtotal C$ {snapshot.Total:N2} · impuestos C$ 0.00 · total C$ {snapshot.Total:N2} · método {snapshot.MetodoPago}.{cash}{reference}";
    }
    public Task<bool> ApplyCurrentShiftAsync(CajaTurnoSnapshot? snapshot, CancellationToken ct) => Apply(snapshot is null ? "No hay turno abierto." : $"Turno actual {snapshot.IdTurnoCaja}: {snapshot.Estado}; efectivo esperado C$ {snapshot.EfectivoEsperado:N2}.", ct);
    public Task<bool> ApplyAccountAndShiftAsync(CajaCuentaRecoverySnapshot account, CajaTurnoSnapshot shift, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !Vigente() || !Ocupado) return Task.FromResult(false);
        InvalidarCuentas();
        return Apply($"Cuenta {account.IdPedido}: {account.Estado}, total C$ {account.Total:N2}, {(account.Cobrable ? "cobrable" : account.Motivo)}. Turno {shift.IdTurnoCaja}: {shift.Estado}. Revise el detalle completo antes de cobrar.", ct);
    }
    public async Task<bool> ApplyPaymentRejectionAsync(string code, CajaCuentaRecoverySnapshot account, CajaTurnoSnapshot shift, CancellationToken ct)
    {
        if (code != "REFERENCIA_REQUIERE_CONFIRMACION" || !await ApplyAccountAndShiftAsync(account, shift, ct)) return false;
        _referenceCollisionApplied = _externalAttempt is { } attempt && attempt.Account == account.IdPedido && attempt.Shift == shift.IdTurnoCaja &&
            attempt.Generation == _openedGeneration && _store?.Read()?.Operacion == attempt.Operation;
        return true;
    }
    public Task<bool> ApplyBarrierAsync(string attemptedType, CajaTurnoSnapshot? currentShift, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !Vigente() || !Ocupado) return Task.FromResult(false);
        InvalidarCuentas();
        Comprobantes.Clear();
        _nextReceiptsCursor = null;
        HayMasComprobantes = false;
        _turnoActual = null;
        ResumenTurno = currentShift is null ? "No hay turno abierto." : DescribeTurno(currentShift);
        _turnoConsultadoSinAbierto = false;
        ConfirmarApertura = false;
        return Apply($"Barrera sin intención ({attemptedType}). {ResumenTurno} Revise nuevamente cuentas y comprobantes antes de cualquier operación.", ct);
    }

    private Task<bool> Apply(string value, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !Vigente() || !Ocupado) return Task.FromResult(false);
        Fotografia = value;
        return Task.FromResult(true);
    }
}
