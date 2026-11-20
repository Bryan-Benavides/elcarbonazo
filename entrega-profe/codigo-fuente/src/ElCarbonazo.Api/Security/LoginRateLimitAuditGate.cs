using System.Collections.Concurrent;

namespace ElCarbonazo.Api.Security;

public sealed class LoginRateLimitAuditGate(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, long> _recordedWindows = new(StringComparer.Ordinal);

    public bool TryBeginWindow(string ipAddress)
    {
        var window = timeProvider.GetUtcNow().ToUnixTimeSeconds() / 60;
        foreach (var stale in _recordedWindows.Where(pair => pair.Value < window - 1).Select(pair => pair.Key))
        {
            _recordedWindows.TryRemove(stale, out _);
        }

        return _recordedWindows.TryAdd($"{ipAddress}:{window}", window);
    }
}
