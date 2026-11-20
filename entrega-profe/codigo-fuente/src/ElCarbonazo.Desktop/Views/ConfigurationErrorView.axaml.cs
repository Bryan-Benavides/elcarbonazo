using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ElCarbonazo.Desktop.Views;

public partial class ConfigurationErrorView : UserControl
{
    public ConfigurationErrorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
