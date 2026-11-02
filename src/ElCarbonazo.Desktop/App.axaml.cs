using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ElCarbonazo.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // La navegación a LoginView se hará en T4.
        // Aquí solo inicializamos la aplicación sin ventana principal.
        base.OnFrameworkInitializationCompleted();
    }
}