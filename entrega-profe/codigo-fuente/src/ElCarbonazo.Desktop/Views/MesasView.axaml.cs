using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ElCarbonazo.Desktop.Views;

public partial class MesasView : UserControl
{
    public MesasView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
