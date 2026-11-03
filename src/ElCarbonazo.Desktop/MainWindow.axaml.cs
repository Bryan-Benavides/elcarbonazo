using Avalonia.Markup.Xaml;
using SukiUI.Controls;

namespace ElCarbonazo.Desktop;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
