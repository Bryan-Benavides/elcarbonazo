using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ElCarbonazo.Desktop.Views;

public partial class SecurityErrorView : UserControl
{
    public SecurityErrorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
