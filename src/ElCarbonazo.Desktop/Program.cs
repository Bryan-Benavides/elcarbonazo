using Avalonia;
using System;
using ElCarbonazo.Desktop.Services;

namespace ElCarbonazo.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args is ["--aprovisionar-diario-caja"])
        {
            Environment.ExitCode = ProvisionCaja();
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static int ProvisionCaja()
    {
        if (!OperatingSystem.IsWindows()) return 1;
        try
        {
            var configuration = new DesktopConfigurationService().Load();
            var directory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ElCarbonazo", "recovery");
            return WindowsCajaJournalProvisioner.CreateFresh(directory, configuration.IdInstalacion) ? 0 : 1;
        }
        catch (Exception ex) when (ex is DesktopConfigurationException or System.IO.IOException or UnauthorizedAccessException)
        {
            return 1;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
