using System.Net;
using System.Security.Authentication;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class TransportAndClockTests
{
    [Fact]
    public async Task TlsFailureIsReportedAsSecurityFailure()
    {
        using var client = new ApiClientService(new DesktopConfiguration(new Uri("https://localhost/"), Guid.NewGuid()),
            new DesktopJsonLogger(), new FailingHandler(new HttpRequestException("TLS", new AuthenticationException())));
        var notified = false;
        client.SecurityFailure += (_, _) => notified = true;
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/mesas");
        await Assert.ThrowsAsync<SecurityConnectionException>(() => client.SendAsync(request, ApiRequestKind.Read));
        Assert.True(notified);
    }

    [Fact]
    public async Task OrdinaryNetworkFailureIsNotReportedAsTlsFailure()
    {
        using var client = new ApiClientService(new DesktopConfiguration(new Uri("https://localhost/"), Guid.NewGuid()),
            new DesktopJsonLogger(), new FailingHandler(new HttpRequestException("offline")));
        var notified = false;
        client.SecurityFailure += (_, _) => notified = true;
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/mesas");
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request, ApiRequestKind.Read));
        Assert.False(notified);
    }

    [Fact]
    public void ServerClockUsesRequestMidpointAndManaguaTimezone()
    {
        var now = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var clock = new ServerClock(new FixedTimeProvider(now));
        clock.Synchronize(now.AddSeconds(11), now, now.AddSeconds(2));
        Assert.Equal(now.AddSeconds(10), clock.UtcNow);
        Assert.Equal(TimeSpan.FromHours(-6), clock.ToManaguaTime(now).Offset);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FailingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(error);
    }
}
