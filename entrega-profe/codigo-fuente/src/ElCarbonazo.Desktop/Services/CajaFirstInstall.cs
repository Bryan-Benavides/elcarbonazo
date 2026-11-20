namespace ElCarbonazo.Desktop.Services;

// The application calls this only for explicit first installation; it never repairs an existing journal.
public static class CajaFirstInstall
{
    public static bool Create(string applicationDirectory)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var directory = Path.GetFullPath(applicationDirectory);
            var configuration = new DesktopConfigurationService(Path.Combine(directory, "desktop.json")).Load();
            return WindowsCajaJournalProvisioner.CreateFresh(Path.Combine(directory, "recovery"), configuration.IdInstalacion);
        }
        catch (Exception ex) when (ex is DesktopConfigurationException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
