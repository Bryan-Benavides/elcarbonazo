namespace ElCarbonazo.Desktop.ViewModels;

public sealed class SecurityErrorViewModel(string message) : ViewModelBase
{
    public string Message { get; } = message;
}
