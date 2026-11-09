using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;

namespace ElCarbonazo.Desktop.Design;

public sealed class CocinaDesignViewModel : CocinaViewModel
{
    public CocinaDesignViewModel() : base(new DesignSession(), new DesignApi(), () => { }, action => { action(); return Task.CompletedTask; }) { }

    private sealed class DesignApi : ICocinaApiService
    {
        public Task<CocinaPaginaDto> ConsultarAsync(string? cursor = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CocinaPaginaDto([new CocinaComandaDto(1, 1, 4, 4, null, 1, DateTimeOffset.UtcNow,
                "Abierta", 1, [new CocinaLineaDto(1, "Producto DEMO", 2, "Sin sal")])], null));
        public Task<CocinaComandaDto> CambiarEstadoAsync(CocinaComandaDto comanda, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DesignSession : ISessionManager
    {
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged { add { } remove { } }
        public bool HasSession => true;
        public SessionLifecycleState State => SessionLifecycleState.Active;
        public SessionSnapshot? CurrentSession => null;
        public Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EvaluateState() { }
    }
}
