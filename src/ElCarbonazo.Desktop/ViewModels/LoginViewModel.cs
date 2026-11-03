using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElCarbonazo.Desktop.Services;

namespace ElCarbonazo.Desktop.ViewModels;

public partial class LoginViewModel(
    ISessionManager sessionManager,
    INavigationService navigation) : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _contrasena = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    private bool CanLogin() =>
        !IsBusy && !string.IsNullOrWhiteSpace(Nombre) && !string.IsNullOrEmpty(Contrasena);

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ErrorMessage = null;
        LoginCommand.NotifyCanExecuteChanged();
        try
        {
            var session = await sessionManager.LoginAsync(Nombre, Contrasena, cancellationToken);
            Contrasena = string.Empty;
            navigation.Navigate(new MesasViewModel(session.Nombre, session.Rol));
        }
        catch (ApiException exception) when (
            exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests)
        {
            Contrasena = string.Empty;
            ErrorMessage = exception.Problem?.Code == "AUTH_DEMASIADOS_INTENTOS"
                ? "Demasiados intentos. Espere un minuto antes de volver a intentar."
                : "Nombre o contraseña inválidos.";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "La API no respondió dentro del tiempo esperado.";
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "No fue posible conectar con la API en la red local.";
        }
        catch (SecurityConnectionException)
        {
            // El transporte informa el bloqueo TLS a SessionManager y ShellViewModel.
        }
        catch (InvalidDataException)
        {
            ErrorMessage = "La API devolvió una respuesta no válida.";
        }
        catch (InvalidOperationException)
        {
            ErrorMessage = "La sesión cambió de estado. Vuelva a intentarlo.";
        }
        finally
        {
            IsBusy = false;
            LoginCommand.NotifyCanExecuteChanged();
        }
    }
}
