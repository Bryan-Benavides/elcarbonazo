namespace ElCarbonazo.Desktop.ViewModels;

// Destino mínimo de T4. El mapa interactivo se implementa exclusivamente en T5.
public sealed class MesasViewModel(string nombre, string rol) : ViewModelBase
{
    public string Nombre { get; } = nombre;
    public string Rol { get; } = rol;
}
