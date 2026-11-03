namespace ElCarbonazo.Desktop.ViewModels;

public sealed class ConfigurationErrorViewModel(string configurationPath, string reason) : ViewModelBase
{
    public string ConfigurationPath { get; } = configurationPath;
    public string Reason { get; } = reason;
}
