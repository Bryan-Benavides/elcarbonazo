using System.Net;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class LoginViewModelTests
{
    [Fact]
    public async Task SuccessNavigatesAndClearsPassword()
    {
        var session = new FakeSession();
        var navigation = new FakeNavigation();
        var vm = new LoginViewModel(session, navigation) { Nombre = "Ana", Contrasena = "secreto", MostrarContrasena = true };
        await vm.LoginCommand.ExecuteAsync(null);
        var mesas = Assert.IsType<MesasViewModel>(navigation.Current);
        Assert.Equal("Ana", mesas.Nombre);
        Assert.Equal("", vm.Contrasena);
        Assert.False(vm.MostrarContrasena);
        Assert.False(vm.IsBusy);
        mesas.Dispose();
    }

    [Fact]
    public void PasswordVisibilityStartsHiddenAndHasAccessibleLabel()
    {
        var vm = new LoginViewModel(new FakeSession(), new FakeNavigation());
        Assert.False(vm.MostrarContrasena);
        Assert.Equal("Mostrar contraseña", vm.TextoVisibilidadContrasena);
        vm.MostrarContrasena = true;
        Assert.Equal("Ocultar contraseña", vm.TextoVisibilidadContrasena);
    }

    [Theory]
    [InlineData(401, "Nombre o contraseña inválidos.")]
    [InlineData(429, "Demasiados intentos. Espere un minuto antes de volver a intentar.")]
    public async Task RejectedLoginShowsSafeMessageAndClearsPassword(int status, string expected)
    {
        var session = new FakeSession
        {
            Failure = new ApiException((HttpStatusCode)status,
            new ApiProblem(status, status == 429 ? "AUTH_DEMASIADOS_INTENTOS" : "AUTH_CREDENCIALES_INVALIDAS", null, null, null))
        };
        var navigation = new FakeNavigation();
        var vm = new LoginViewModel(session, navigation) { Nombre = "Ana", Contrasena = "secreto", MostrarContrasena = true };
        await vm.LoginCommand.ExecuteAsync(null);
        Assert.Equal(expected, vm.ErrorMessage);
        Assert.Equal("", vm.Contrasena);
        Assert.False(vm.MostrarContrasena);
        Assert.Null(navigation.Current);
    }

    [Fact]
    public async Task NetworkFailureDoesNotNavigateOrRetainPassword()
    {
        var navigation = new FakeNavigation();
        var vm = new LoginViewModel(new FakeSession { Failure = new HttpRequestException() }, navigation)
        { Nombre = "Ana", Contrasena = "secreto" };
        await vm.LoginCommand.ExecuteAsync(null);
        Assert.Contains("conectar", vm.ErrorMessage);
        Assert.Null(navigation.Current);
        Assert.Equal("", vm.Contrasena);
    }

    private sealed class FakeNavigation : INavigationService
    {
        public event EventHandler<ViewModelBase>? Navigated;
        public ViewModelBase? Current { get; private set; }
        public void Navigate(ViewModelBase viewModel) { Current = viewModel; Navigated?.Invoke(this, viewModel); }
    }

    private sealed class FakeSession : ISessionManager
    {
        public Exception? Failure { get; init; }
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged { add { } remove { } }
        public bool HasSession => false;
        public SessionLifecycleState State => SessionLifecycleState.SignedOut;
        public SessionSnapshot? CurrentSession => null;
        public Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            return Failure is not null ? Task.FromException<AuthSessionResponse>(Failure) : Task.FromResult(
                new AuthSessionResponse("access", "refresh", now.AddMinutes(5), now.AddMinutes(15), now.AddHours(8), now, 1, nombre, "Mesero"));
        }
        public Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EvaluateState() { }
    }
}
