using Avalonia.Markup.Xaml;
using ElCarbonazo.Desktop.Controls;

namespace ElCarbonazo.Desktop;

public partial class MainWindow : VentanaPrincipal
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
