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

    [Fact]
    public async Task ConcurrentHumanInteractionsShareOneRefresh()
    {
        var api = new DelayedRefreshApi();
        using var manager = new SessionManager(api, new ServerClock(), new DesktopJsonLogger());
        await manager.LoginAsync("mesero", "password");
        var first = manager.NotifyHumanInteractionAsync();
        await api.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = manager.NotifyHumanInteractionAsync();
        Assert.Equal(1, api.RefreshCalls);
        api.RefreshResponse.SetResult(Response(Session("new-access", "new-refresh")));
        await Task.WhenAll(first, second);
        Assert.Equal(1, api.RefreshCalls);
        Assert.True(manager.HasSession);
    }

    [Fact]
    public async Task InactivityWarningAndExpiryClearSession()
    {
        var clock = new MutableClock { UtcNow = DateTimeOffset.UtcNow };
        var api = new DelayedRefreshApi();
        using var manager = new SessionManager(api, clock, new DesktopJsonLogger());
        await manager.LoginAsync("mesero", "password");
        clock.UtcNow = DateTimeOffset.UtcNow.AddMinutes(14).AddSeconds(10);
        manager.EvaluateState();
        Assert.Equal(SessionLifecycleState.Expiring, manager.State);
        clock.UtcNow = DateTimeOffset.UtcNow.AddMinutes(16);
        manager.EvaluateState();
        Assert.Equal(SessionLifecycleState.Expired, manager.State);
        Assert.False(manager.HasSession);
        Assert.Null(manager.CurrentSession);
    }

    private sealed class MutableClock : IServerClock
    {
        public DateTimeOffset UtcNow { get; set; }
        public void Synchronize(DateTimeOffset serverTime, DateTimeOffset sentAt, DateTimeOffset receivedAt) { }
        public DateTimeOffset ToManaguaTime(DateTimeOffset utcInstant) => utcInstant;
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
        public int RefreshCalls;
        public event EventHandler<SecurityFailureEventArgs>? SecurityFailure { add { } remove { } }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, ApiRequestKind requestKind, CancellationToken cancellationToken = default)
        {
            return request.RequestUri?.ToString() switch
            {
                "api/auth/login" => Task.FromResult(Response(Session("access", "refresh"))),
                "api/auth/refresh" => Refresh(),
                "api/auth/logout" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)),
                "api/auth/activity" => Task.FromException<HttpResponseMessage>(new HttpRequestException()),
                _ => throw new InvalidOperationException("Solicitud inesperada.")
            };
        }

        private Task<HttpResponseMessage> Refresh()
        {
            Interlocked.Increment(ref RefreshCalls);
            RefreshStarted.TrySetResult();
            return RefreshResponse.Task;
        }
    }
}
