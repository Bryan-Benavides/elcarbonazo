using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;

namespace ElCarbonazo.Desktop.ViewModels;

public partial class ShellViewModel : ViewModelBase, IDisposable
{
    private readonly INavigationService _navigation;
    private readonly ISessionManager? _sessionManager;
    private readonly IServerClock? _serverClock;
    private readonly CancellationTokenSource _lifetime = new();

    [ObservableProperty]
    private ViewModelBase _currentViewModel;

    [ObservableProperty]
    private bool _isInactivityWarningVisible;

    [ObservableProperty]
    private string _inactivityCountdown = string.Empty;

    [ObservableProperty]
    private string? _sessionActionError;

    [ObservableProperty]
    private bool _isAbsoluteWarningVisible;

    [ObservableProperty]
    private string _absoluteWarningText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSessionPaused))]
    private SessionLifecycleState _sessionState;

    public bool IsSessionPaused => SessionState == SessionLifecycleState.Paused;

    public ShellViewModel(
        ViewModelBase initialViewModel,
        INavigationService navigation,
        ISessionManager? sessionManager,
        IServerClock? serverClock)
    {
        _currentViewModel = initialViewModel;
        _navigation = navigation;
        _sessionManager = sessionManager;
        _serverClock = serverClock;
        _navigation.Navigated += OnNavigated;
        if (_sessionManager is not null)
        {
            _sessionState = _sessionManager.State;
            _sessionManager.SessionStateChanged += OnSessionStateChanged;
            _ = MonitorSessionAsync(_lifetime.Token);
        }
    }

    [RelayCommand]
    private async Task ContinueSessionAsync()
    {
        if (_sessionManager is null)
        {
            return;
        }

        SessionActionError = null;
        try
        {
            await _sessionManager.ContinueSessionAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (SecurityConnectionException)
        {
            // El evento de sesión navega a la pantalla de seguridad.
        }
        catch (Exception)
        {
            SessionActionError = "No fue posible continuar la sesión. Verifique la conexión e inténtelo de nuevo.";
        }
        UpdateWarnings();
    }

    [RelayCommand]
    private async Task EndSessionAsync()
    {
        if (_sessionManager is not null)
        {
            await _sessionManager.LogoutAsync(_lifetime.Token);
        }
    }

    private async Task MonitorSessionAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await Dispatcher.UIThread.InvokeAsync(UpdateWarnings);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateWarnings()
    {
        _sessionManager?.EvaluateState();
        if (_sessionManager?.CurrentSession is not { } session || _serverClock is null)
        {
            IsInactivityWarningVisible = false;
            IsAbsoluteWarningVisible = false;
            SessionActionError = null;
            return;
        }

        var now = _serverClock.UtcNow;
        var inactivityRemaining = session.InactivityExpiresAt - now;
        var absoluteRemaining = session.AbsoluteSessionExpiresAt - now;
        IsInactivityWarningVisible = inactivityRemaining <= TimeSpan.FromMinutes(1);
        InactivityCountdown = $"La sesión se cerrará en {Math.Max(0, (int)Math.Ceiling(inactivityRemaining.TotalSeconds))} s";
        IsAbsoluteWarningVisible = absoluteRemaining <= TimeSpan.FromMinutes(5);
        AbsoluteWarningText = $"La sesión finalizará a las {_serverClock.ToManaguaTime(session.AbsoluteSessionExpiresAt):hh:mm tt}. Este límite no puede extenderse.";
    }

    private void OnNavigated(object? sender, ViewModelBase viewModel) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (CurrentViewModel is IDisposable disposable && !ReferenceEquals(CurrentViewModel, viewModel))
            {
                disposable.Dispose();
            }
            CurrentViewModel = viewModel;
        });

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs args) =>
        Dispatcher.UIThread.Post(() => ApplySessionState(args));

    private void ApplySessionState(SessionStateChangedEventArgs args)
    {
        SessionState = args.State;
        UpdateWarnings();
        switch (args.State)
        {
            case SessionLifecycleState.SignedOut:
            case SessionLifecycleState.Expired:
                NavigateToLogin();
                break;
            case SessionLifecycleState.SecurityBlocked:
                _navigation.Navigate(new SecurityErrorViewModel(
                    args.Message ?? "La conexión segura con el servidor no pudo validarse."));
                break;
        }
    }

    private void NavigateToLogin()
    {
        if (_sessionManager is not null && CurrentViewModel is not SecurityErrorViewModel)
        {
            _navigation.Navigate(new LoginViewModel(_sessionManager, _navigation));
        }
    }

    public void Dispose()
    {
        (CurrentViewModel as IDisposable)?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        _navigation.Navigated -= OnNavigated;
        if (_sessionManager is not null)
        {
            _sessionManager.SessionStateChanged -= OnSessionStateChanged;
        }
    }
}
