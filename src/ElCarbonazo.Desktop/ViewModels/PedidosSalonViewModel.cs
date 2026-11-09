using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;

namespace ElCarbonazo.Desktop.ViewModels;

/// <summary>Consulta de salón y envío explícito con intención retenida ante incertidumbre.</summary>
public partial class PedidosSalonViewModel : ViewModelBase, IDisposable
{
    private readonly IProductosPedidoApiService _productos;
    private readonly ICuentaPedidoApiService _cuentas;
    private readonly IEnvioPedidoApiService _envios;
    private readonly Action _volver;
    private readonly int? _mesaId;
    private readonly int? _grupoId;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _operacion = new(1, 1);
    private readonly List<ProductoPedidoDto> _catalogo = [];
    private uint? _versionMesa;
    private IntencionPedido? _intencion;
    private bool _cuentaConsultada;

    [ObservableProperty] private string _categoria = "Comida";
    [ObservableProperty] private string _estadoLectura = "Consultando catálogo y cuenta…";
    [ObservableProperty] private CuentaPedidoDto? _cuenta;
    [ObservableProperty] private bool _conectado;
    [ObservableProperty] private string _mensaje = "El borrador es local y aún no se ha enviado.";
    [ObservableProperty] private bool _confirmandoEnvio;
    [ObservableProperty] private bool _enviando;
    [ObservableProperty] private bool _resultadoIncierto;

    public string Destino { get; }
    public ObservableCollection<ProductoPedidoDto> Productos { get; } = [];
    public ObservableCollection<LineaSalonViewModel> Borrador { get; } = [];
    public decimal Subtotal => Borrador.Sum(line => line.Cantidad * line.Producto.Precio);
    public string SubtotalTexto => $"C$ {Subtotal:N2}";
    public bool HayCuenta => Cuenta is not null;
    public bool CuentaLegada => Cuenta?.SoloLectura == true;
    public string EstadoCuenta => !_cuentaConsultada ? "Cuenta aún no consultada" :
        Cuenta is null ? "Sin cuenta activa" : $"Cuenta {Cuenta.IdPedido} · {Cuenta.Estado}";
    public string TotalCuenta => !_cuentaConsultada ? "—" : Cuenta is null ? "C$ 0.00" : $"C$ {Cuenta.Total:N2}";
    public bool HayListas => Cuenta?.Comandas.Any(command => command.Estado == "Lista") == true;
    public bool CategoriaComida => Categoria == "Comida";
    public bool CategoriaBebida => Categoria == "Bebida";
    public bool CategoriaLicor => Categoria == "Licor";
    public bool CatalogoVacio => Conectado && Productos.Count == 0;
    public bool BorradorDesactualizado => Conectado && Borrador.Any(line => !_catalogo.Any(product =>
        product.IdProducto == line.Producto.IdProducto && product.Version == line.Producto.Version &&
        product.Precio == line.Producto.Precio && product.Nombre == line.Producto.Nombre));
    public bool PuedeEditar => Conectado && !Enviando && !ResultadoIncierto && !ConfirmandoEnvio;
    public bool PuedeVolver => !Enviando && !ResultadoIncierto;
    public bool PuedeAgregar => Conectado && !CuentaLegada && PuedeEditar && Borrador.Count < 50;
    public bool PuedeEnviar => Conectado && !CuentaLegada && PuedeEditar && Borrador.Count is > 0 and <= 50 &&
        (_grupoId is not null || _versionMesa is not null) && Borrador.All(line => line.Cantidad is >= 1 and <= 99 &&
            line.NotaValida && _catalogo.Any(product => product.IdProducto == line.Producto.IdProducto &&
                product.Version == line.Producto.Version && product.Precio == line.Producto.Precio && product.Nombre == line.Producto.Nombre));
    public bool PuedeRepetir => Conectado && ResultadoIncierto && !Enviando && _intencion is not null;

    public PedidosSalonViewModel(int? mesaId, int? grupoId, string destino,
        IProductosPedidoApiService productos, ICuentaPedidoApiService cuentas, Action volver,
        IEnvioPedidoApiService? envios = null, uint? versionMesa = null)
    {
        _mesaId = mesaId;
        _grupoId = grupoId;
        Destino = destino;
        _productos = productos;
        _cuentas = cuentas;
        _envios = envios ?? new EnvioNoDisponible();
        _versionMesa = versionMesa;
        _volver = volver;
        _ = ConsultarPeriodicamenteAsync(_lifetime.Token);
    }

    partial void OnCategoriaChanged(string value)
    {
        AplicarCategoria();
        OnPropertyChanged(nameof(CategoriaComida));
        OnPropertyChanged(nameof(CategoriaBebida));
        OnPropertyChanged(nameof(CategoriaLicor));
    }
    partial void OnCuentaChanged(CuentaPedidoDto? value)
    {
        OnPropertyChanged(nameof(HayCuenta));
        OnPropertyChanged(nameof(CuentaLegada));
        OnPropertyChanged(nameof(EstadoCuenta));
        OnPropertyChanged(nameof(TotalCuenta));
        OnPropertyChanged(nameof(HayListas));
        OnPropertyChanged(nameof(PuedeAgregar));
        ActualizarPermisos();
    }
    partial void OnConectadoChanged(bool value) => ActualizarPermisos();
    partial void OnEnviandoChanged(bool value) => ActualizarPermisos();
    partial void OnResultadoInciertoChanged(bool value) => ActualizarPermisos();
    partial void OnConfirmandoEnvioChanged(bool value) => ActualizarPermisos();

    [RelayCommand]
    private void ElegirCategoria(string? categoria)
    {
        if (categoria is "Comida" or "Bebida" or "Licor") Categoria = categoria;
    }

    [RelayCommand]
    private void Agregar(ProductoPedidoDto? producto)
    {
        if (!PuedeAgregar || producto is null) return;
        var actual = _catalogo.SingleOrDefault(item => item.IdProducto == producto.IdProducto);
        if (actual is null || actual.Version != producto.Version) return;
        var line = new LineaSalonViewModel(actual, ActualizarSubtotal);
        Borrador.Add(line);
        ActualizarSubtotal();
        ActualizarPermisos();
    }

    [RelayCommand]
    private void Retirar(LineaSalonViewModel? linea)
    {
        if (!PuedeEditar || linea is null) return;
        Borrador.Remove(linea);
        ActualizarSubtotal();
        ActualizarPermisos();
    }

    [RelayCommand] private void Volver() { if (PuedeVolver) _volver(); }

    [RelayCommand] private void SolicitarEnvio() { if (PuedeEnviar) ConfirmandoEnvio = true; }
    [RelayCommand] private void CancelarEnvio() => ConfirmandoEnvio = false;
    [RelayCommand]
    private async Task ConfirmarEnvioAsync()
    {
        if (!ConfirmandoEnvio) return;
        ConfirmandoEnvio = false;
        if (!PuedeEnviar) return;
        var lines = Borrador.Select(line =>
        {
            NotaPedido.TryNormalizar(line.Notas, out var normalized);
            return new LineaIntencionPedido(line.Producto.IdProducto, line.Producto.Version, line.Cantidad,
                normalized, line.Producto.Nombre, line.Producto.Precio);
        }).ToArray();
        _intencion = new IntencionPedido(_mesaId, _grupoId, _versionMesa, lines);
        await EnviarIntencionAsync(false);
    }

    [RelayCommand] private Task RepetirEnvioAsync() => PuedeRepetir ? EnviarIntencionAsync(true) : Task.CompletedTask;

    private async Task EnviarIntencionAsync(bool repeticion)
    {
        if (_intencion is null || Enviando) return;
        Enviando = true;
        var acquired = false;
        try
        {
            await _operacion.WaitAsync(_lifetime.Token);
            acquired = true;
            if (!repeticion && !Conectado)
            {
                _intencion = null;
                Mensaje = "No se envió la comanda: los datos dejaron de estar disponibles.";
                return;
            }
            if (!repeticion && (_intencion.Lineas.Any(line => !_catalogo.Any(product =>
                    product.IdProducto == line.IdProducto && product.Version == line.VersionProducto &&
                    product.Precio == line.PrecioUnitario && product.Nombre == line.Nombre)) || CuentaLegada))
            {
                _intencion = null;
                Mensaje = "Los datos cambiaron antes de enviar. Revise el borrador y la cuenta.";
                return;
            }
            var confirmation = await _envios.EnviarAsync(_intencion, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            Cuenta = confirmation.Pedido;
            _versionMesa = confirmation.VersionMesa ?? _versionMesa;
            Borrador.Clear();
            _intencion = null;
            ResultadoIncierto = false;
            Mensaje = "Comanda confirmada por la API.";
            ActualizarSubtotal();
        }
        catch (ApiException ex) when ((int)ex.StatusCode < 500 && !repeticion)
        {
            _intencion = null;
            if (ex.Problem?.Code == "MESA_CONFLICTO_CONCURRENCIA") _versionMesa = null;
            Mensaje = ex.Problem?.Code switch
            {
                "CATALOGO_CAMBIADO" => "El catálogo cambió. Revise precios y disponibilidad; retire y agregue nuevamente las líneas afectadas.",
                "MESA_CONFLICTO_CONCURRENCIA" => "La mesa cambió. Vuelva al mapa y abra de nuevo la cuenta antes de enviar.",
                "CUENTA_LEGADA_SIN_COMANDAS" => "La cuenta anterior es de solo lectura y bloquea nuevos envíos.",
                _ => "La API rechazó el envío. Revise la cuenta antes de intentar otra acción."
            };
            Conectado = false;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            ResultadoIncierto = true;
            Conectado = false;
            EstadoLectura = "Consulta pendiente · los datos pueden estar desactualizados.";
            Mensaje = "Resultado incierto: pudo aplicarse. Una consulta no demuestra que terminó. Solo puede repetir manualmente esta misma solicitud; no se reintentó automáticamente. Si cierra la aplicación, perderá esta intención y necesitará revisión humana.";
        }
        finally
        {
            if (acquired) _operacion.Release();
            Enviando = false;
            ActualizarPermisos();
        }
    }

    private void ActualizarSubtotal()
    {
        OnPropertyChanged(nameof(SubtotalTexto));
        ActualizarPermisos();
    }

    private void ActualizarPermisos()
    {
        OnPropertyChanged(nameof(PuedeEditar));
        OnPropertyChanged(nameof(PuedeVolver));
        OnPropertyChanged(nameof(PuedeAgregar));
        OnPropertyChanged(nameof(PuedeEnviar));
        OnPropertyChanged(nameof(PuedeRepetir));
        OnPropertyChanged(nameof(BorradorDesactualizado));
        OnPropertyChanged(nameof(CatalogoVacio));
    }

    private void AplicarCategoria()
    {
        Productos.Clear();
        foreach (var item in _catalogo.Where(item => item.Categoria == Categoria)) Productos.Add(item);
        OnPropertyChanged(nameof(CatalogoVacio));
    }

    private async Task ConsultarPeriodicamenteAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _operacion.WaitAsync(cancellationToken);
                    try
                    {
                        var catalog = new List<ProductoPedidoDto>();
                        var cursors = new HashSet<string>(StringComparer.Ordinal);
                        string? cursor = null;
                        do
                        {
                            var page = await _productos.ConsultarAsync(cursor, cancellationToken);
                            catalog.AddRange(page.Items);
                            cursor = page.CursorSiguiente;
                            if (cursor is not null && (!cursors.Add(cursor) || cursors.Count > 100))
                                throw new InvalidDataException("La paginación de catálogo no progresa.");
                        } while (cursor is not null);
                        if (catalog.Select(x => x.IdProducto).Distinct().Count() != catalog.Count)
                            throw new InvalidDataException("El catálogo contiene productos repetidos entre páginas.");
                        var account = await _cuentas.ConsultarAsync(_mesaId, _grupoId, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        _catalogo.Clear();
                        _catalogo.AddRange(catalog);
                        AplicarCategoria();
                        _cuentaConsultada = true;
                        Cuenta = account.Pedido;
                        OnPropertyChanged(nameof(EstadoCuenta));
                        OnPropertyChanged(nameof(TotalCuenta));
                        Conectado = true;
                        EstadoLectura = "Datos actualizados · consulta local";
                        ActualizarPermisos();
                        failures = 0;
                    }
                    finally { _operacion.Release(); }
                }
                catch (SessionExpiredException)
                {
                    Conectado = false;
                    EstadoLectura = "Sesión finalizada. Los datos pueden estar desactualizados.";
                    return;
                }
                catch (SecurityConnectionException)
                {
                    Conectado = false;
                    EstadoLectura = "Conexión segura no disponible. Los datos pueden estar desactualizados.";
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    Conectado = false;
                    EstadoLectura = ex is SessionPausedException
                        ? "Sesión en pausa. Interactúe para continuar."
                        : "Sin datos actuales · se conserva la última consulta.";
                    failures = Math.Min(failures + 1, 4);
                }
                await Task.Delay(failures switch { 0 => 2000, 1 => 2000, 2 => 5000, 3 => 10000, _ => 30000 }, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public void Dispose() => _lifetime.Cancel();

    private sealed class EnvioNoDisponible : IEnvioPedidoApiService
    {
        public Task<EnvioConfirmadoPedidoDto> EnviarAsync(IntencionPedido intencion, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Servicio de envío no configurado.");
    }
}

public partial class LineaSalonViewModel : ObservableObject
{
    private readonly Action _changed;
    public ProductoPedidoDto Producto { get; }
    [ObservableProperty] private int _cantidad = 1;
    [ObservableProperty] private string _notas = string.Empty;
    public string Importe => $"C$ {Cantidad * Producto.Precio:N2}";
    public bool NotaValida => NotaPedido.TryNormalizar(Notas, out _);
    public string AyudaNota => NotaValida ? "Hasta 150 caracteres Unicode" : "Nota inválida o superior a 150 caracteres.";

    public LineaSalonViewModel(ProductoPedidoDto producto, Action changed) { Producto = producto; _changed = changed; }
    partial void OnCantidadChanged(int value) { OnPropertyChanged(nameof(Importe)); _changed(); }
    partial void OnNotasChanged(string value) { OnPropertyChanged(nameof(NotaValida)); OnPropertyChanged(nameof(AyudaNota)); _changed(); }
    [RelayCommand] private void Incrementar() { if (Cantidad < 99) Cantidad++; }
    [RelayCommand] private void Decrementar() { if (Cantidad > 1) Cantidad--; }
}
