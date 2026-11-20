using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;

namespace ElCarbonazo.Desktop.ViewModels;

public partial class InventarioLecturaViewModel : ViewModelBase, IDisposable
{
    private readonly IInventarioLecturaService? _reader;
    private readonly ISessionManager? _session;
    private readonly Action _volver;
    private readonly InventarioEscrituraCoordinator? _writes;
    private readonly CancellationTokenSource _lifetime = new();
    private string? _ingredientsNext;
    private string? _productsNext;
    private string? _movimientosNext;
    private bool _disposed;
    private long _sessionGeneration;
    private int _ingredientsFresh;
    private long _ingredientFilterGeneration;
    private long _productFilterGeneration;

    public ObservableCollection<IngredienteLectura> Ingredientes { get; } = [];
    public ObservableCollection<ProductoRecetaLectura> Productos { get; } = [];
    public ObservableCollection<MovimientoLectura> Movimientos { get; } = [];
    public IReadOnlyList<string> EstadosFiltro { get; } = ["Todos", "Normal", "StockBajo", "Agotado"];
    public bool PuedeVerRecetas { get; }
    public bool HayMasIngredientes => _ingredientsNext is not null;
    public bool HayMasProductos => _productsNext is not null;
    public bool HayMasMovimientos => _movimientosNext is not null;

    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private string _estado = "Consulte inventario para ver datos actuales.";
    [ObservableProperty] private string _detalleReceta = "Seleccione un producto y consulte su receta.";
    [ObservableProperty] private string _resumenAlertas = "Resumen aún no consultado.";
    [ObservableProperty] private ProductoRecetaLectura? _productoSeleccionado;
    [ObservableProperty] private string _filtroEstado = "Todos";
    [ObservableProperty] private string _busquedaIngredientes = "";
    [ObservableProperty] private string _busquedaProductos = "";
    [ObservableProperty] private string _estadoMovimientos = "Seleccione un ingrediente y consulte su kardex.";

    partial void OnFiltroEstadoChanged(string value) => InvalidarFiltro();
    partial void OnBusquedaIngredientesChanged(string value) => InvalidarFiltro();

    private void InvalidarFiltro()
    {
        Interlocked.Exchange(ref _ingredientsFresh, 0);
        Interlocked.Increment(ref _ingredientFilterGeneration);
        _ingredientsNext = null;
        OnPropertyChanged(nameof(HayMasIngredientes));
    }

    partial void OnBusquedaProductosChanged(string value)
    {
        Interlocked.Increment(ref _productFilterGeneration);
        Interlocked.Exchange(ref _recipeFresh, 0);
        _productsNext = null;
        ConfirmarReceta = false;
        OnPropertyChanged(nameof(HayMasProductos));
    }

    public InventarioLecturaViewModel() : this(null, null, false, () => { }) { }

    public InventarioLecturaViewModel(IInventarioLecturaService? reader, ISessionManager? session, bool puedeVerRecetas, Action volver,
        InventarioEscrituraCoordinator? writes = null)
    {
        _reader = reader;
        _session = session;
        PuedeVerRecetas = puedeVerRecetas;
        _volver = volver;
        _writes = writes;
        if (_writes?.ResultadoIncierto == true)
            EstadoEscritura = "Hay una operación incierta anterior. Las modificaciones siguen bloqueadas; una consulta no demuestra que terminó.";
        if (_session is not null) _session.SessionStateChanged += OnSessionStateChanged;
    }

    [RelayCommand]
    private void Volver() { if (!Ocupado && !_disposed) _volver(); }

    [RelayCommand]
    private Task ConsultarIngredientesAsync() => CargarIngredientesAsync(false);

    [RelayCommand]
    private async Task ConsultarAlertasAsync()
    {
        if (!CanRead()) return;
        Ocupado = true;
        var generation = Interlocked.Read(ref _sessionGeneration);
        try
        {
            var summary = await _reader!.AlertasAsync(_lifetime.Token);
            if (CanApply(generation))
                ResumenAlertas = $"Activos: {summary.TotalActivos} · Stock bajo: {summary.StockBajo} · Agotados: {summary.Agotados}";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { if (!_disposed) ResumenAlertas = "No se pudo consultar el resumen; no se confirma el estado actual."; }
        finally { if (!_disposed) Ocupado = false; }
    }

    [RelayCommand]
    private Task SiguientesIngredientesAsync() => CargarIngredientesAsync(true);

    private async Task CargarIngredientesAsync(bool next)
    {
        if (!CanRead() || (next && _ingredientsNext is null)) return;
        Ocupado = true;
        var generation = Interlocked.Read(ref _sessionGeneration);
        var filterGeneration = Interlocked.Read(ref _ingredientFilterGeneration);
        try
        {
            var filter = FiltroEstado == "Todos" ? null : FiltroEstado;
            var search = string.IsNullOrWhiteSpace(BusquedaIngredientes) ? null : BusquedaIngredientes.Trim();
            var page = filter is null && search is null
                ? await _reader!.IngredientesAsync(next ? _ingredientsNext : null, _lifetime.Token)
                : await _reader!.IngredientesFiltradosAsync(next ? _ingredientsNext : null, filter, search, _lifetime.Token);
            if (!CanApply(generation) || Interlocked.Read(ref _ingredientFilterGeneration) != filterGeneration ||
                (FiltroEstado == "Todos" ? null : FiltroEstado) != filter ||
                (string.IsNullOrWhiteSpace(BusquedaIngredientes) ? null : BusquedaIngredientes.Trim()) != search) return;
            Ingredientes.Clear();
            foreach (var item in page.Items) Ingredientes.Add(item);
            Interlocked.Exchange(ref _ingredientsFresh, 1);
            _ingredientsNext = page.Siguiente;
            OnPropertyChanged(nameof(HayMasIngredientes));
            Estado = "Consulta de inventario actualizada. Las páginas pueden reflejar momentos distintos.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { Interlocked.Exchange(ref _ingredientsFresh, 0); if (!_disposed) Estado = "No se pudo consultar inventario; los datos visibles pueden estar desactualizados."; }
        finally { if (!_disposed) Ocupado = false; }
    }

    [RelayCommand] private Task ConsultarMovimientosAsync() => CargarMovimientosAsync(false);
    [RelayCommand] private Task SiguientesMovimientosAsync() => CargarMovimientosAsync(true);

    private async Task CargarMovimientosAsync(bool next)
    {
        if (!CanRead() || IngredienteSeleccionado is not { } ingredient || !Ingredientes.Contains(ingredient) ||
            (next && _movimientosNext is null)) return;
        Ocupado = true;
        var generation = Interlocked.Read(ref _sessionGeneration);
        try
        {
            var page = await _reader!.MovimientosAsync(ingredient.Id, next ? _movimientosNext : null, _lifetime.Token);
            if (!CanApply(generation) || IngredienteSeleccionado?.Id != ingredient.Id) return;
            Movimientos.Clear();
            foreach (var movement in page.Items) Movimientos.Add(movement);
            _movimientosNext = page.Siguiente;
            OnPropertyChanged(nameof(HayMasMovimientos));
            EstadoMovimientos = $"Kardex de {ingredient.Nombre} · las páginas pueden reflejar momentos distintos.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { if (!_disposed) EstadoMovimientos = "No se pudo consultar el kardex; los datos visibles pueden estar desactualizados."; }
        finally { if (!_disposed) Ocupado = false; }
    }

    [RelayCommand] private Task ConsultarProductosAsync() => CargarProductosAsync(false);
    [RelayCommand] private Task SiguientesProductosAsync() => CargarProductosAsync(true);

    private async Task CargarProductosAsync(bool next)
    {
        if (!PuedeVerRecetas || !CanRead() || (next && _productsNext is null)) return;
        Interlocked.Exchange(ref _recipeFresh, 0);
        ConfirmarReceta = false;
        Ocupado = true;
        var generation = Interlocked.Read(ref _sessionGeneration);
        var filterGeneration = Interlocked.Read(ref _productFilterGeneration);
        try
        {
            var search = string.IsNullOrWhiteSpace(BusquedaProductos) ? null : BusquedaProductos.Trim();
            var page = search is null
                ? await _reader!.ProductosAsync(next ? _productsNext : null, _lifetime.Token)
                : await _reader!.ProductosFiltradosAsync(next ? _productsNext : null, search, _lifetime.Token);
            if (!CanApply(generation) || Interlocked.Read(ref _productFilterGeneration) != filterGeneration) return;
            ProductoSeleccionado = null;
            DetalleReceta = "Seleccione un producto y consulte su receta.";
            Productos.Clear();
            foreach (var item in page.Items) Productos.Add(item);
            _productsNext = page.Siguiente;
            OnPropertyChanged(nameof(HayMasProductos));
            Estado = "Catálogo de recetas actualizado.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { if (!_disposed) Estado = "No se pudieron consultar recetas; los datos visibles pueden estar desactualizados."; }
        finally { if (!_disposed) Ocupado = false; }
    }

    [RelayCommand]
    private async Task ConsultarRecetaAsync()
    {
        if (!PuedeVerRecetas || !CanRead() || ProductoSeleccionado is not { } product) return;
        Interlocked.Exchange(ref _recipeFresh, 0);
        ConfirmarReceta = false;
        Ocupado = true;
        var generation = Interlocked.Read(ref _sessionGeneration);
        try
        {
            var detail = await _reader!.DetalleRecetaAsync(product.Id, _lifetime.Token);
            if (CanApply(generation) && ProductoSeleccionado?.Id == product.Id)
            {
                AplicarReceta(detail);
                DetalleReceta = !detail.Existe ? "Producto sin receta configurada." :
                    string.Join(Environment.NewLine, new[] { detail.Activa ? "Receta activa" : "Receta inactiva" }.Concat(detail.Lineas.Select(x => x.Texto)));
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { if (!_disposed) DetalleReceta = "No se pudo consultar la receta; no se confirma su estado actual."; }
        finally { if (!_disposed) Ocupado = false; }
    }

    private bool CanRead() => !_disposed && !Ocupado && _reader is not null && _session?.State is SessionLifecycleState.Active or SessionLifecycleState.Expiring;
    private bool CanApply(long generation) => !_disposed && !_lifetime.IsCancellationRequested &&
        Interlocked.Read(ref _sessionGeneration) == generation &&
        _session?.State is SessionLifecycleState.Active or SessionLifecycleState.Expiring;

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs args)
    {
        Interlocked.Increment(ref _sessionGeneration);
        Interlocked.Exchange(ref _ingredientsFresh, 0);
        Interlocked.Exchange(ref _recipeFresh, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_session is not null) _session.SessionStateChanged -= OnSessionStateChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
