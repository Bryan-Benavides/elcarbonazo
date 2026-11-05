using System.Net;
using System.Net.Http.Json;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class SessionBehaviorTests
{
    [Fact]
    public async Task PollingDoesNotRefreshOrReportHumanActivity()
    {
        var api = new FakeApi(HttpStatusCode.OK);
        using var session = new SessionManager(api, new ServerClock(), new DesktopJsonLogger());
        await session.LoginAsync("mesero", "password");
        using var response = await session.SendPollingAsync(() => new HttpRequestMessage(HttpMethod.Get, "api/mesas"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, api.RefreshCalls);
        Assert.Equal(0, api.ActivityCalls);
    }

    [Fact]
    public async Task ForbiddenDoesNotTriggerRefresh()
    {
        var api = new FakeApi(HttpStatusCode.Forbidden);
        using var session = new SessionManager(api, new ServerClock(), new DesktopJsonLogger());
        await session.LoginAsync("mesero", "password");
        using var response = await session.SendHumanAsync(() => new HttpRequestMessage(HttpMethod.Put, "api/mesas/1/estado"), false);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, api.RefreshCalls);
    }

    [Fact]
    public async Task FailedRefreshThrowsInsteadOfReturningDisposedResponse()
    {
        var api = new FakeApi(HttpStatusCode.Unauthorized);
        using var session = new SessionManager(api, new ServerClock(), new DesktopJsonLogger());
        await session.LoginAsync("mesero", "password");
        await Assert.ThrowsAsync<SessionExpiredException>(() => session.SendHumanAsync(
            () => new HttpRequestMessage(HttpMethod.Put, "api/mesas/1/estado"), false));
        Assert.Equal(1, api.RefreshCalls);
        Assert.Equal(1, api.MutationCalls);
        Assert.Equal(SessionLifecycleState.Expired, session.State);
    }

    [Fact]
    public async Task ExplicitHumanActivityIsThrottled()
    {
        var api = new FakeApi(HttpStatusCode.OK) { ActivitySuccess = true };
        var clock = new MutableClock { UtcNow = DateTimeOffset.UtcNow };
        using var session = new SessionManager(api, clock, new DesktopJsonLogger());
        await session.LoginAsync("mesero", "password");
        clock.UtcNow = clock.UtcNow.AddSeconds(61);
        await session.ContinueSessionAsync();
        await session.ContinueSessionAsync();
        Assert.Equal(1, api.ActivityCalls);
    }

    private sealed class MutableClock : IServerClock
    {
        public DateTimeOffset UtcNow { get; set; }
        public void Synchronize(DateTimeOffset serverTime, DateTimeOffset sentAt, DateTimeOffset receivedAt) { }
        public DateTimeOffset ToManaguaTime(DateTimeOffset utcInstant) => utcInstant;
    }

    [Fact]
    public async Task LogoutClearsSessionEvenWhenServerCannotBeReached()
    {
        var api = new FakeApi(HttpStatusCode.OK);
        using var session = new SessionManager(api, new ServerClock(), new DesktopJsonLogger());
        await session.LoginAsync("mesero", "password");
        await session.LogoutAsync();
        Assert.Equal(SessionLifecycleState.SignedOut, session.State);
        Assert.False(session.HasSession);
        Assert.Null(session.CurrentSession);
    }

    [Fact]
    public async Task TlsSecurityEventBlocksAndClearsSession()
    {
        var api = new FakeApi(HttpStatusCode.OK);
        using var session = new SessionManager(api, new ServerClock(), new DesktopJsonLogger());
        await session.LoginAsync("mesero", "password");
        api.BlockTls();
        Assert.Equal(SessionLifecycleState.SecurityBlocked, session.State);
        Assert.False(session.HasSession);
        Assert.Null(session.CurrentSession);
    }

    private sealed class FakeApi(HttpStatusCode mesaStatus) : IApiClientService
    {
        public event EventHandler<SecurityFailureEventArgs>? SecurityFailure;
        public void BlockTls() => SecurityFailure?.Invoke(this, new SecurityFailureEventArgs("TLS inválido"));
        public int RefreshCalls;
        public int ActivityCalls;
        public int MutationCalls;
        public bool ActivitySuccess { get; init; }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, ApiRequestKind requestKind, CancellationToken cancellationToken = default)
        {
            var path = request.RequestUri?.ToString();
            var now = DateTimeOffset.UtcNow;
            return path switch
            {
                "api/auth/login" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new AuthSessionResponse("access", "refresh", now.AddMinutes(5), now.AddMinutes(15), now.AddHours(8), now, 1, "mesero", "Mesero"))
                }),
                "api/auth/refresh" => Refresh(),
                "api/auth/activity" => Activity(),
                "api/auth/logout" => Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")),
                _ => Mesa(request)
            };
        }

        private Task<HttpResponseMessage> Refresh()
        {
            Interlocked.Increment(ref RefreshCalls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = JsonContent.Create(new ApiProblem(401, "AUTH_SESION_EXPIRADA", null, null, null)) });
        }
        private Task<HttpResponseMessage> Activity()
        {
            Interlocked.Increment(ref ActivityCalls);
            if (ActivitySuccess)
            {
                var now = DateTimeOffset.UtcNow;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new ActivityResponse(now.AddMinutes(15), now.AddHours(8), now))
                });
            }
            return Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"));
        }
        private Task<HttpResponseMessage> Mesa(HttpRequestMessage request)
        {
            if (request.Method == HttpMethod.Put) Interlocked.Increment(ref MutationCalls);
            return Task.FromResult(new HttpResponseMessage(mesaStatus)
            {
                Content = mesaStatus == HttpStatusCode.Unauthorized
                    ? JsonContent.Create(new ApiProblem(401, "AUTH_ACCESS_TOKEN_EXPIRADO", null, null, null))
                    : JsonContent.Create(Array.Empty<object>())
            });
        }
    }
}
