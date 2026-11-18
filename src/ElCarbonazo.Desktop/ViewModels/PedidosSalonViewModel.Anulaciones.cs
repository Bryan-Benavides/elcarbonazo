using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;

namespace ElCarbonazo.Desktop.ViewModels;

public sealed class LineaAnulableViewModel
{
    public LineaAnulableViewModel(int idComanda, DetallePedidoDto line, Action<LineaAnulableViewModel> solicitar)
    {
        IdComanda = idComanda;
        IdDetalle = line.IdDetalle;
        NombreProducto = line.NombreProducto;
        Cantidad = line.Cantidad;
        Subtotal = line.Subtotal;
        SolicitarCommand = new RelayCommand(() => solicitar(this));
    }

    public int IdComanda { get; }
    public int IdDetalle { get; }
    public string NombreProducto { get; }
    public short Cantidad { get; }
    public decimal Subtotal { get; }
    public string Texto => $"{NombreProducto} · {Cantidad} · C$ {Subtotal:N2}";
    public IRelayCommand SolicitarCommand { get; }
}

public sealed class ComandaAnulableViewModel
{
    private Action<ComandaAnulableViewModel>? _solicitar;

    public ComandaAnulableViewModel(int idComanda, string estado)
    {
        IdComanda = idComanda;
        Estado = estado;
    }

    public int IdComanda { get; }
    public string Estado { get; }
    public string Titulo => $"Comanda #{IdComanda} · {Estado}";
    public ObservableCollection<LineaAnulableViewModel> Lineas { get; } = [];
    public decimal Total => Lineas.Sum(line => line.Subtotal);
    public IRelayCommand? SolicitarCommand { get; private set; }

    public void Inicializar(Action<ComandaAnulableViewModel> solicitar)
    {
        _solicitar = solicitar;
        SolicitarCommand = new RelayCommand(() => _solicitar(this));
    }
}

/// <summary>Flujo de anulación de línea o comanda con autorización superior y diario local.</summary>
public partial class PedidosSalonViewModel
{
    private readonly AnulacionCoordinator? _anulaciones;
    private readonly Func<string?> _rol;
    private int? _idComandaSeleccionada;
    private int? _idDetalleSeleccionado;
    private Guid? _operacionPendiente;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeAnular))]
    private bool _diarioAnulacionDisponible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeAnular))]
    [NotifyPropertyChangedFor(nameof(PuedeConfirmarAnulacion))]
    private bool _confirmandoAnulacion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeAnular))]
    [NotifyPropertyChangedFor(nameof(PuedeConfirmarAnulacion))]
    [NotifyPropertyChangedFor(nameof(PuedeRecuperarAnulacion))]
    [NotifyPropertyChangedFor(nameof(PuedeEstablecerBarrera))]
    private bool _anulando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeAnular))]
    [NotifyPropertyChangedFor(nameof(PuedeRecuperarAnulacion))]
    [NotifyPropertyChangedFor(nameof(PuedeEstablecerBarrera))]
    private bool _anulacionIncierta;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeAnular))]
    [NotifyPropertyChangedFor(nameof(PuedeRecuperarAnulacion))]
    private bool _anulacionTerminalPendienteConciliar;

    [ObservableProperty] private bool _confirmandoBarrera;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayMensajeAnulacion))]
    private string _mensajeAnulacion = "";

    [ObservableProperty] private string _resumenAnulacion = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeConfirmarAnulacion))]
    private string _motivoAnulacion = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeConfirmarAnulacion))]
    private string _emailJefa = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeConfirmarAnulacion))]
    private string _contrasenaJefa = "";

    public ObservableCollection<ComandaAnulableViewModel> ComandasAnulables { get; } = [];

    public bool RolPuedeAnular => _rol() is "Jefa" or "Supervisor" or "Mesero" or "Encargado de Caja";

    public bool PuedeAnular => _anulaciones is not null && DiarioAnulacionDisponible && RolPuedeAnular &&
        Conectado && !CuentaLegada && !Enviando && !Anulando && !ConfirmandoAnulacion &&
        !AnulacionIncierta && !AnulacionTerminalPendienteConciliar &&
        Cuenta is { Estado: not "Pagado" } account && account.Comandas.Count > 0;

    public bool PuedeConfirmarAnulacion => ConfirmandoAnulacion && !Anulando &&
        AnulacionCoordinator.NormalizarMotivo(MotivoAnulacion) is not null &&
        !string.IsNullOrWhiteSpace(EmailJefa) && EmailJefa.Trim().Length <= 254 && EmailJefa.Contains('@') &&
        !string.IsNullOrEmpty(ContrasenaJefa) && ContrasenaJefa.Length <= 256;

    public bool PuedeCancelarAnulacion => ConfirmandoAnulacion && !Anulando;

    public bool PuedeRecuperarAnulacion => _anulaciones is not null && DiarioAnulacionDisponible &&
        (AnulacionIncierta || AnulacionTerminalPendienteConciliar) && !Anulando;

    public bool PuedeEstablecerBarrera => PuedeRecuperarAnulacion && !ConfirmandoBarrera;

    public bool ConfirmandoBarreraVisible => ConfirmandoBarrera;

    public bool HayMensajeAnulacion => !string.IsNullOrWhiteSpace(MensajeAnulacion);

    private void InicializarAnulacion()
    {
        if (_anulaciones is null)
        {
            MensajeAnulacion = "Anulación no disponible en este equipo.";
            return;
        }
        var state = _anulaciones.Estado();
        if (state is null)
        {
            MensajeAnulacion = "Anulación no disponible: falta el diario protegido o no se puede verificar. " +
                               "No se crea un diario vacío para desbloquearla.";
            return;
        }
        DiarioAnulacionDisponible = true;
        if (state.Estado == "Pendiente")
        {
            _operacionPendiente = state.Operacion;
            AnulacionIncierta = true;
            MensajeAnulacion = "Hay una anulación pendiente registrada antes de enviar. Consulte la operación; " +
                               "no repita el envío ni cree una anulación nueva.";
        }
        else if (state.Estado == "TerminalPorConciliar")
        {
            _operacionPendiente = state.Operacion;
            AnulacionTerminalPendienteConciliar = true;
            MensajeAnulacion = "Hay una anulación terminal pendiente de conciliar. Actualice la cuenta.";
        }
    }

    private void ReconstruirAnulables()
    {
        ComandasAnulables.Clear();
        if (Cuenta is null) return;
        foreach (var command in Cuenta.Comandas.Where(command =>
                     command.Estado is "Abierta" or "En Preparación" or "Lista" && command.Lineas.Count > 0))
        {
            var comanda = new ComandaAnulableViewModel(command.IdComanda, command.Estado);
            foreach (var line in command.Lineas)
                comanda.Lineas.Add(new LineaAnulableViewModel(command.IdComanda, line, SolicitarAnulacionLinea));
            comanda.Inicializar(SolicitarAnulacionComanda);
            ComandasAnulables.Add(comanda);
        }
        OnPropertyChanged(nameof(PuedeAnular));
    }

    private void SolicitarAnulacionLinea(LineaAnulableViewModel line)
    {
        if (!PuedeAnular) return;
        _idComandaSeleccionada = line.IdComanda;
        _idDetalleSeleccionado = line.IdDetalle;
        ResumenAnulacion = $"Anular la línea «{line.NombreProducto}» (cantidad {line.Cantidad}) por C$ {line.Subtotal:N2}.";
        AbrirModalAnulacion();
    }

    private void SolicitarAnulacionComanda(ComandaAnulableViewModel command)
    {
        if (!PuedeAnular) return;
        _idComandaSeleccionada = command.IdComanda;
        _idDetalleSeleccionado = null;
        ResumenAnulacion = $"Anular la comanda #{command.IdComanda} completa " +
                           $"({command.Lineas.Count} líneas) por C$ {command.Total:N2}.";
        AbrirModalAnulacion();
    }

    private void AbrirModalAnulacion()
    {
        MotivoAnulacion = "";
        EmailJefa = "";
        ContrasenaJefa = "";
        MensajeAnulacion = "";
        ConfirmandoAnulacion = true;
        ActualizarPermisos();
    }

    [RelayCommand]
    private void CancelarAnulacion()
    {
        if (!PuedeCancelarAnulacion) return;
        ConfirmandoAnulacion = false;
        ConfirmandoBarrera = false;
        MotivoAnulacion = "";
        EmailJefa = "";
        ContrasenaJefa = "";
        ActualizarPermisos();
    }

    [RelayCommand]
    private async Task ConfirmarAnulacionAsync()
    {
        if (!PuedeConfirmarAnulacion || Cuenta is not { } account || _anulaciones is null ||
            _idComandaSeleccionada is not int commandId) return;
        var motive = MotivoAnulacion;
        var email = EmailJefa;
        var password = ContrasenaJefa;
        var intention = new IntencionAnulacion(Guid.NewGuid(), account.IdPedido, commandId,
            _idDetalleSeleccionado, motive);
        Anulando = true;
        try
        {
            var result = await _anulaciones.AnularAsync(intention, email, password, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            switch (result.Estado)
            {
                case EstadoResultadoAnulacion.Completada:
                case EstadoResultadoAnulacion.Rechazada:
                    _operacionPendiente = intention.IdOperacion;
                    AnulacionTerminalPendienteConciliar = true;
                    ConfirmandoAnulacion = false;
                    MensajeAnulacion = result.Mensaje;
                    break;
                case EstadoResultadoAnulacion.Incierta:
                    _operacionPendiente = intention.IdOperacion;
                    AnulacionIncierta = true;
                    ConfirmandoAnulacion = false;
                    MensajeAnulacion = result.Mensaje;
                    Conectado = false;
                    EstadoLectura = "Anulación con resultado incierto · no repita el envío.";
                    break;
                default:
                    MensajeAnulacion = result.Mensaje;
                    break;
            }
        }
        catch (SessionExpiredException)
        {
            AnulacionIncierta = true;
            ConfirmandoAnulacion = false;
            MensajeAnulacion = "La sesión expiró durante la anulación. No se confirmó efecto; consulte la operación.";
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidDataException)
        {
            AnulacionIncierta = true;
            ConfirmandoAnulacion = false;
            MensajeAnulacion = "Resultado incierto: no se pudo confirmar la anulación. No repita el envío; consulte la operación.";
        }
        finally
        {
            ContrasenaJefa = "";
            Anulando = false;
            ActualizarPermisos();
        }
    }

    [RelayCommand]
    private async Task ConsultarOperacionAnulacionAsync()
    {
        if (!PuedeRecuperarAnulacion || _anulaciones is null) return;
        Anulando = true;
        try
        {
            var result = await _anulaciones.RecuperarAsync(false, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            MensajeAnulacion = result.Mensaje;
            if (result.EsTerminal)
            {
                AnulacionIncierta = false;
                AnulacionTerminalPendienteConciliar = true;
            }
        }
        catch (SessionExpiredException)
        {
            MensajeAnulacion = "La sesión expiró. Vuelva a iniciar sesión para consultar la operación.";
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidDataException)
        {
            MensajeAnulacion = "No se pudo consultar la operación. No repita la anulación; reintente la consulta.";
        }
        finally
        {
            Anulando = false;
            ActualizarPermisos();
        }
    }

    [RelayCommand]
    private void SolicitarBarrera()
    {
        if (!PuedeEstablecerBarrera) return;
        ConfirmandoBarrera = true;
        OnPropertyChanged(nameof(ConfirmandoBarreraVisible));
    }

    [RelayCommand]
    private void CancelarBarrera()
    {
        ConfirmandoBarrera = false;
        OnPropertyChanged(nameof(ConfirmandoBarreraVisible));
    }

    [RelayCommand]
    private async Task ConfirmarBarreraAsync()
    {
        if (!ConfirmandoBarrera || Anulando || _anulaciones is null) return;
        Anulando = true;
        try
        {
            var result = await _anulaciones.RecuperarAsync(true, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            MensajeAnulacion = result.Mensaje;
            if (result.EsTerminal)
            {
                AnulacionIncierta = false;
                AnulacionTerminalPendienteConciliar = true;
            }
            ConfirmandoBarrera = false;
            OnPropertyChanged(nameof(ConfirmandoBarreraVisible));
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidDataException or SessionExpiredException)
        {
            MensajeAnulacion = "No se pudo establecer la barrera. No repita la anulación; reintente la consulta.";
        }
        finally
        {
            Anulando = false;
            ActualizarPermisos();
        }
    }

    private void ConciliarAnulacionTrasLectura()
    {
        if (!AnulacionTerminalPendienteConciliar || _operacionPendiente is not Guid operation ||
            _anulaciones?.Conciliar(operation) != true) return;
        AnulacionTerminalPendienteConciliar = false;
        _operacionPendiente = null;
        if (!string.IsNullOrWhiteSpace(MensajeAnulacion))
            MensajeAnulacion = $"{MensajeAnulacion} Diario conciliado con la cuenta actual.";
    }

    partial void OnAnulacionInciertaChanged(bool value) => ActualizarPermisos();
    partial void OnAnulandoChanged(bool value) => ActualizarPermisos();
    partial void OnConfirmandoAnulacionChanged(bool value) => ActualizarPermisos();
    partial void OnAnulacionTerminalPendienteConciliarChanged(bool value) => ActualizarPermisos();
}
