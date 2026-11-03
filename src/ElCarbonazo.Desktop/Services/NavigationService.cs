using ElCarbonazo.Desktop.ViewModels;

namespace ElCarbonazo.Desktop.Services;

public interface INavigationService
{
    event EventHandler<ViewModelBase>? Navigated;
    void Navigate(ViewModelBase viewModel);
}

public sealed class NavigationService : INavigationService
{
    public event EventHandler<ViewModelBase>? Navigated;

    public void Navigate(ViewModelBase viewModel) => Navigated?.Invoke(this, viewModel);
}
