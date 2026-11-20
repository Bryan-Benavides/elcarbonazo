namespace ElCarbonazo.Desktop.Services;

// Explicit first installation only; it never repairs or replaces an existing anulaciones journal.
public static class AnulacionFirstInstall
{
    public static bool Create(string applicationDirectory)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var directory = Path.GetFullPath(applicationDirectory);
            var configuration = new DesktopConfigurationService(Path.Combine(directory, "desktop.json")).Load();
            return WindowsAnulacionJournalProvisioner.CreateFresh(
                Path.Combine(directory, "recovery", "anulaciones"), configuration.IdInstalacion);
        }
        catch (Exception ex) when (ex is DesktopConfigurationException or IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
