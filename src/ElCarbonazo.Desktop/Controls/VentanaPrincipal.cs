using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using SukiUI.Controls;

namespace ElCarbonazo.Desktop.Controls;

// SukiUI 7.0.1 intercepta el hit test de su botón para Snap Layout en Windows.
// Se sustituye ese botón, sin alterar los demás controles ni la lógica de la vista.
public class VentanaPrincipal : SukiWindow
{
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var original = e.NameScope.Find<Button>("PART_MaximizeButton");
        if (original?.Parent is not StackPanel panel)
            return;

        original.IsVisible = false;
        var button = new Button
        {
            Content = new PathIcon
            {
                Width = TitleBarControlSize,
                Height = TitleBarControlSize,
                Data = Geometry.Parse("M 3,3 L 17,3 L 17,17 L 3,17 Z M 5,5 L 5,15 L 15,15 L 15,5 Z")
            }
        };
        button.Classes.Add("WindowControlsButton");
        WindowDecorationProperties.SetElementRole(button, WindowDecorationsElementRole.User);
        AutomationProperties.SetName(button, "Maximizar o restaurar ventana");
        ToolTip.SetTip(button, "Maximizar o restaurar");
        button.Click += (_, _) =>
        {
            if (CanMaximize && WindowState != WindowState.FullScreen)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        };
        panel.Children.Insert(panel.Children.IndexOf(original), button);
    }
}
