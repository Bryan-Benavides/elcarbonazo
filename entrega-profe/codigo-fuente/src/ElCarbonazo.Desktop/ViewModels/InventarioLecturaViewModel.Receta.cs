using System.Collections.ObjectModel;
using System.Net.Http.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElCarbonazo.Desktop.Services;

namespace ElCarbonazo.Desktop.ViewModels;

public partial class InventarioLecturaViewModel
{
    private int _recipeFresh;
    private int _recipeProduct;
    private string? _recipeVersion;
    public ObservableCollection<LineaRecetaLectura> LineasReceta { get; } = [];
    [ObservableProperty] private string _cantidadReceta = "";
    [ObservableProperty] private string _notasReceta = "";
    [ObservableProperty] private bool _recetaActiva = true;
    [ObservableProperty] private bool _confirmarReceta;

    partial void OnProductoSeleccionadoChanged(ProductoRecetaLectura? value)
    {
        Interlocked.Exchange(ref _recipeFresh, 0);
        ConfirmarReceta = false;
        LineasReceta.Clear();
        DetalleReceta = "Seleccione un producto y consulte su receta.";
    }

    partial void OnCantidadRecetaChanged(string value) => ConfirmarReceta = false;
    partial void OnNotasRecetaChanged(string value) => ConfirmarReceta = false;
    partial void OnRecetaActivaChanged(bool value) => ConfirmarReceta = false;

    private void AplicarReceta(RecetaLectura recipe)
    {
        Interlocked.Exchange(ref _recipeFresh, 0);
        _recipeProduct = recipe.ProductoId;
        _recipeVersion = recipe.Version;
        LineasReceta.Clear();
        foreach (var line in recipe.Lineas) LineasReceta.Add(line);
        NotasReceta = recipe.Notas ?? "";
        RecetaActiva = !recipe.Existe || recipe.Activa;
        ConfirmarReceta = false;
        Interlocked.Exchange(ref _recipeFresh, 1);
    }

    [RelayCommand]
    private void AgregarLineaReceta()
    {
        ConfirmarReceta = false;
        if (!PuedeAdministrar || !CanRead() || Volatile.Read(ref _recipeFresh) == 0 ||
            Volatile.Read(ref _ingredientsFresh) == 0 || IngredienteSeleccionado is not { Activo: true } ingredient ||
            !Ingredientes.Contains(ingredient) || LineasReceta.Count >= 50 ||
            LineasReceta.Any(x => x.Id == ingredient.Id) || !TryCantidad(CantidadReceta, false, out var quantity))
        {
            EstadoEscritura = "Consulte receta e ingredientes, seleccione uno activo y use cantidad válida sin duplicados.";
            return;
        }
        LineasReceta.Add(new(ingredient.Id, ingredient.Nombre, ingredient.Unidad, quantity, true));
        CantidadReceta = "";
    }

    [RelayCommand]
    private void QuitarLineaReceta(LineaRecetaLectura? line)
    {
        if (!PuedeAdministrar || Ocupado || line is null || Volatile.Read(ref _recipeFresh) == 0) return;
        ConfirmarReceta = false;
        LineasReceta.Remove(line);
    }

    [RelayCommand]
    private async Task GuardarRecetaAsync()
    {
        if (!PuedeAdministrar || !CanRead() || !ConfirmarReceta || _writes!.ResultadoIncierto ||
            Volatile.Read(ref _recipeFresh) == 0 || ProductoSeleccionado is not { } product ||
            product.Id != _recipeProduct || !Productos.Contains(product)) return;
        ConfirmarReceta = false;
        if ((RecetaActiva && LineasReceta.Count == 0) || LineasReceta.Count > 50 ||
            LineasReceta.Any(x => !x.Activo || x.Cantidad <= 0) ||
            LineasReceta.Select(x => x.Id).Distinct().Count() != LineasReceta.Count ||
            NotasReceta.Length > 500 || NotasReceta.Any(char.IsControl))
        {
            EstadoEscritura = "Revise las líneas activas, cantidades y notas de la receta.";
            return;
        }
        var lines = LineasReceta.Select(x => new { idIngrediente = x.Id, cantidadRequerida = x.Cantidad }).ToArray();
        var version = _recipeVersion;
        var notes = string.IsNullOrWhiteSpace(NotasReceta) ? null : NotasReceta.Trim();
        var active = RecetaActiva;
        Ocupado = true;
        try
        {
            var result = await _writes.EnviarAsync(() => new HttpRequestMessage(HttpMethod.Put, $"api/recetas/producto/{product.Id}")
            {
                Content = JsonContent.Create(new { activa = active, notasPreparacion = notes, versionEsperada = version, lineas = lines })
            }, _lifetime.Token);
            AplicarResultado(result);
        }
        finally { if (!_disposed) Ocupado = false; }
    }
}
