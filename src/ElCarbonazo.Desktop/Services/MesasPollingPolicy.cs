namespace ElCarbonazo.Desktop.Services;

public static class MesasPollingPolicy
{
    private static readonly int[] RetrySeconds = [2, 5, 10, 30];
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    public static TimeSpan DelayAfterSuccess(TimeSpan elapsed) =>
        elapsed >= Interval ? TimeSpan.Zero : Interval - elapsed;

    public static TimeSpan DelayAfterFailure(int consecutiveFailures)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consecutiveFailures);
        return TimeSpan.FromSeconds(RetrySeconds[Math.Min(consecutiveFailures - 1, RetrySeconds.Length - 1)]);
    }
}
