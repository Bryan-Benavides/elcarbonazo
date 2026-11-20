using System.Globalization;
using System.Net.Http.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;

namespace ElCarbonazo.Desktop.ViewModels;

public partial class InventarioLecturaViewModel
{
    public IReadOnlyList<string> Unidades { get; } = ["unidad", "lb", "kg", "oz", "g", "ml", "lt"];
    public IReadOnlyList<string> TiposMovimiento { get; } = ["EntradaCompra", "AjustePositivo", "AjusteNegativo"];
    public bool PuedeAdministrar => PuedeVerRecetas && _writes is not null;
    [ObservableProperty] private string _nombreNuevo = "";
    [ObservableProperty] private string _unidadNueva = "unidad";
    [ObservableProperty] private string _minimoNuevo = "0";
    [ObservableProperty] private bool _confirmarIngrediente;
    [ObservableProperty] private IngredienteLectura? _ingredienteSeleccionado;
    [ObservableProperty] private string _tipoMovimiento = "EntradaCompra";
    [ObservableProperty] private string _cantidadMovimiento = "";
    [ObservableProperty] private string _motivoMovimiento = "";
    [ObservableProperty] private bool _confirmarMovimiento;
    [ObservableProperty] private string _nombreEdicion = "";
    [ObservableProperty] private string _unidadEdicion = "unidad";
    [ObservableProperty] private string _minimoEdicion = "0";
    [ObservableProperty] private bool _activoEdicion;
    [ObservableProperty] private bool _confirmarEdicion;
    [ObservableProperty] private string _estadoEscritura = "Ninguna operación administrativa pendiente.";

    partial void OnNombreNuevoChanged(string value) => ConfirmarIngrediente = false;
    partial void OnUnidadNuevaChanged(string value) => ConfirmarIngrediente = false;
    partial void OnMinimoNuevoChanged(string value) => ConfirmarIngrediente = false;
    partial void OnIngredienteSeleccionadoChanged(IngredienteLectura? value)
    {
        ConfirmarMovimiento = false;
        ConfirmarEdicion = false;
        _movimientosNext = null;
        Movimientos.Clear();
        EstadoMovimientos = "Seleccione un ingrediente y consulte su kardex.";
        OnPropertyChanged(nameof(HayMasMovimientos));
        NombreEdicion = value?.Nombre ?? "";
        UnidadEdicion = value?.Unidad ?? "unidad";
        MinimoEdicion = value?.Minimo.ToString("0.####", CultureInfo.InvariantCulture) ?? "0";
        ActivoEdicion = value?.Activo ?? false;
    }
    partial void OnNombreEdicionChanged(string value) => ConfirmarEdicion = false;
    partial void OnUnidadEdicionChanged(string value) => ConfirmarEdicion = false;
    partial void OnMinimoEdicionChanged(string value) => ConfirmarEdicion = false;
    partial void OnActivoEdicionChanged(bool value) => ConfirmarEdicion = false;
    partial void OnTipoMovimientoChanged(string value) => ConfirmarMovimiento = false;
    partial void OnCantidadMovimientoChanged(string value) => ConfirmarMovimiento = false;
    partial void OnMotivoMovimientoChanged(string value) => ConfirmarMovimiento = false;

    [RelayCommand]
    private async Task CrearIngredienteAsync()
    {
        if (!PuedeAdministrar || !ConfirmarIngrediente || !CanRead() || _writes!.ResultadoIncierto) return;
        ConfirmarIngrediente = false;
        var name = NombreNuevo.Trim();
        if (name.Length is < 1 or > 120 || name.Any(char.IsControl) || !Unidades.Contains(UnidadNueva) ||
            !TryCantidad(MinimoNuevo, true, out var minimum))
        {
            EstadoEscritura = "Revise nombre, unidad y mínimo (hasta cuatro decimales con punto).";
            return;
        }
        Ocupado = true;
        try
        {
            var result = await _writes.EnviarAsync(() => new HttpRequestMessage(HttpMethod.Post, "api/inventario/ingredientes")
            {
                Content = JsonContent.Create(new { nombre = name, unidadMedida = UnidadNueva, stockMinimo = minimum })
            }, _lifetime.Token);
            AplicarResultado(result);
        }
        finally { if (!_disposed) Ocupado = false; }
    }

    [RelayCommand]
    private async Task ActualizarIngredienteAsync()
    {
        if (!PuedeAdministrar || !ConfirmarEdicion || !CanRead() || _writes!.ResultadoIncierto ||
            Volatile.Read(ref _ingredientsFresh) == 0 || IngredienteSeleccionado is not { } ingredient ||
            !Ingredientes.Contains(ingredient)) return;
        ConfirmarEdicion = false;
        var name = NombreEdicion.Trim();
        if (name.Length is < 1 or > 120 || name.Any(char.IsControl) || !Unidades.Contains(UnidadEdicion) ||
            !TryCantidad(MinimoEdicion, true, out var minimum) ||
            !long.TryParse(ingredient.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version <= 0 ||
            ingredient.Version != version.ToString(CultureInfo.InvariantCulture))
        {
            EstadoEscritura = "Revise los datos y vuelva a consultar la versión del ingrediente.";
            return;
        }
        var unit = UnidadEdicion;
        var active = ActivoEdicion;
        Ocupado = true;
        try
        {
            var result = await _writes.EnviarAsync(() => new HttpRequestMessage(HttpMethod.Put, $"api/inventario/ingredientes/{ingredient.Id}")
            {
                Content = JsonContent.Create(new { nombre = name, unidadMedida = unit, stockMinimo = minimum, activo = active, version = ingredient.Version })
            }, _lifetime.Token);
            AplicarResultado(result);
        }
        finally { if (!_disposed) Ocupado = false; }
    }

    [RelayCommand]
    private async Task RegistrarMovimientoAsync()
    {
        if (!PuedeAdministrar || !ConfirmarMovimiento || !CanRead() || _writes!.ResultadoIncierto ||
            Volatile.Read(ref _ingredientsFresh) == 0 || IngredienteSeleccionado is not { Activo: true } ingredient ||
            !Ingredientes.Contains(ingredient)) return;
        ConfirmarMovimiento = false;
        if (!TiposMovimiento.Contains(TipoMovimiento) || !TryCantidad(CantidadMovimiento, false, out var quantity) ||
            (TipoMovimiento != "EntradaCompra" && string.IsNullOrWhiteSpace(MotivoMovimiento)) ||
            MotivoMovimiento.Length > 250 || MotivoMovimiento.Any(char.IsControl) ||
            (TipoMovimiento == "AjusteNegativo" && quantity > ingredient.Stock))
        {
            EstadoEscritura = "Revise tipo, cantidad, motivo y stock consultado antes de confirmar.";
            return;
        }
        var type = TipoMovimiento;
        var reason = string.IsNullOrWhiteSpace(MotivoMovimiento) ? null : MotivoMovimiento.Trim();
        Ocupado = true;
        try
        {
            var result = await _writes.EnviarAsync(() => new HttpRequestMessage(HttpMethod.Post, "api/inventario/movimientos")
            {
                Content = JsonContent.Create(new { idIngrediente = ingredient.Id, tipoMovimiento = type, cantidad = quantity, motivo = reason })
            }, _lifetime.Token);
            AplicarResultado(result);
        }
        finally { if (!_disposed) Ocupado = false; }
    }

    private void AplicarResultado(InventarioEscrituraResultado result)
    {
        Interlocked.Exchange(ref _ingredientsFresh, 0);
        Interlocked.Exchange(ref _recipeFresh, 0);
        if (_disposed) return;
        EstadoEscritura = result.Estado switch
        {
            InventarioEscrituraEstado.Completada => "Operación confirmada. Consulte de nuevo inventario antes de otra modificación.",
            InventarioEscrituraEstado.Rechazada => $"La API rechazó la operación ({result.Codigo}). Consulte y revise antes de confirmar otra.",
            InventarioEscrituraEstado.Incierta => "Resultado incierto: pudo aplicarse. No se reintentó; las modificaciones quedan bloqueadas. Una consulta no demuestra que terminó.",
            _ => "No se envió la operación: sesión no disponible u otra operación bloqueada."
        };
    }

    private static bool TryCantidad(string value, bool zero, out decimal result) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result) &&
        (zero ? result >= 0 : result > 0) && result <= 99999999.9999m && decimal.Round(result, 4) == result;
}
