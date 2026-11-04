using System.Net;
using System.Net.Http.Json;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class SessionRaceTests
{
    [Fact]
    public async Task LateRefreshCannotRestoreSessionAfterLogout()
    {
        var api = new DelayedRefreshApi();
        using var manager = new SessionManager(api, new ServerClock(), new DesktopJsonLogger());
        await manager.LoginAsync("mesero", "password");

        var interaction = manager.NotifyHumanInteractionAsync();
        await api.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.LogoutAsync();
        api.RefreshResponse.SetResult(Response(Session("new-access", "new-refresh")));
        await interaction;

        Assert.Equal(SessionLifecycleState.SignedOut, manager.State);
        Assert.False(manager.HasSession);
        Assert.Null(manager.CurrentSession);
    }

    private static AuthSessionResponse Session(string access, string refresh)
    {
        var now = DateTimeOffset.UtcNow;
        return new AuthSessionResponse(access, refresh, now.AddSeconds(20), now.AddMinutes(15),
            now.AddHours(8), now, 1, "mesero", "Mesero");
    }

    private static HttpResponseMessage Response(AuthSessionResponse session) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(session)
    };

    private sealed class DelayedRefreshApi : IApiClientService
    {
        public TaskCompletionSource RefreshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> RefreshResponse { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<SecurityFailureEventArgs>? SecurityFailure { add { } remove { } }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, ApiRequestKind requestKind, CancellationToken cancellationToken = default)
        {
            return request.RequestUri?.ToString() switch
            {
                "api/auth/login" => Task.FromResult(Response(Session("access", "refresh"))),
                "api/auth/refresh" => Refresh(),
                "api/auth/logout" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)),
                _ => throw new InvalidOperationException("Solicitud inesperada.")
            };
        }

        private Task<HttpResponseMessage> Refresh()
        {
            RefreshStarted.TrySetResult();
            return RefreshResponse.Task;
        }
    }
}
