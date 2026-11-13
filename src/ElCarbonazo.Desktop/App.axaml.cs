using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;

namespace ElCarbonazo.Desktop;

public partial class App : Application
{
    private ApiClientService? _apiClient;
    private SessionManager? _sessionManager;
    private ServerClock? _serverClock;
    private ShellViewModel? _shellViewModel;
    private DesktopJsonLogger? _logger;
    private bool _closeInProgress;
    private bool _closeAuthorized;
    public CajaRecoveryCoordinator? CajaRecovery { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _logger = new DesktopJsonLogger();
            _logger.Information("DesktopStarting", "La aplicación Desktop está iniciando.");
            var configurationService = new DesktopConfigurationService();
            var navigation = new NavigationService();
            ViewModelBase initialViewModel;

            try
            {
                var configuration = configurationService.Load();
                _apiClient = new ApiClientService(configuration, _logger);
                _serverClock = new ServerClock();
                _sessionManager = new SessionManager(_apiClient, _serverClock, _logger);
                if (OperatingSystem.IsWindows())
                {
                    var directory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "ElCarbonazo", "recovery");
                    var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User;
                    if (sid is not null)
                    {
                        var store = new CajaRecoveryStore(System.IO.Path.Combine(directory, "caja-journal.json"),
                            configuration.IdInstalacion, new WindowsCajaRecoverySecurityPolicy(directory, sid));
                        CajaRecovery = new CajaRecoveryCoordinator(store, new CajaRecoveryHttpTransport(_sessionManager),
                            () => _sessionManager.CurrentSession?.IdUsuario);
                    }
                }
                initialViewModel = new LoginViewModel(_sessionManager, navigation);
            }
            catch (DesktopConfigurationException exception)
            {
                _logger.Error("DesktopConfigurationInvalid", exception.Message);
                initialViewModel = new ConfigurationErrorViewModel(
                    configurationService.ConfigurationPath,
                    exception.Message);
            }

            _shellViewModel = new ShellViewModel(
                initialViewModel,
                navigation,
                _sessionManager,
                _serverClock);
            var window = new MainWindow { DataContext = _shellViewModel };
            window.AddHandler(
                InputElement.PointerPressedEvent,
                OnHumanInteraction,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            window.AddHandler(
                InputElement.KeyDownEvent,
                OnHumanInteraction,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            window.Closing += OnWindowClosing;
            desktop.MainWindow = window;
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnHumanInteraction(object? sender, RoutedEventArgs args)
    {
        if (_sessionManager?.HasSession == true && _shellViewModel?.IsInactivityWarningVisible != true)
        {
            _ = ObserveInteractionAsync();
        }
    }

    private async Task ObserveInteractionAsync()
    {
        try
        {
            await _sessionManager!.NotifyHumanInteractionAsync();
        }
        catch (OperationCanceledException)
        {
            // Incluye el cierre de la aplicación y tiempos de espera.
        }
        catch (SecurityConnectionException)
        {
            // El transporte ya informó el bloqueo de seguridad.
        }
        catch (Exception)
        {
            _logger?.Warning("HumanActivityReportFailed", "No fue posible reportar la interacción en este momento.");
        }
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_closeAuthorized || _sessionManager?.HasSession != true)
        {
            return;
        }

        args.Cancel = true;
        if (_closeInProgress)
        {
            return;
        }

        _closeInProgress = true;
        try
        {
            _logger?.Information(
                "DesktopShutdownLogoutStarted",
                "Se inició el cierre de sesión remoto acotado antes de salir.");
            await _sessionManager.LogoutForShutdownAsync();
        }
        catch (Exception)
        {
            // El cierre siempre continúa; LogoutForShutdownAsync ya limpia los tokens en finally.
            _logger?.Warning(
                "DesktopShutdownLogoutFailed",
                "El cierre continuará sin confirmación remota.");
        }
        finally
        {
            _closeAuthorized = true;
            _closeInProgress = false;
            if (sender is Window window)
            {
                window.Close();
            }
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs args)
    {
        _shellViewModel?.Dispose();
        _sessionManager?.Dispose();
        _apiClient?.Dispose();
        _logger?.Information("DesktopStopped", "La aplicación Desktop finalizó.");
    }
}
