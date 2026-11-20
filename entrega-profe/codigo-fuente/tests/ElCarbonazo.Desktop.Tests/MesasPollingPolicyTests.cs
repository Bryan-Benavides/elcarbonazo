using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class MesasPollingPolicyTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(4, 30)]
    [InlineData(20, 30)]
    public void FailureBackoffIsBounded(int failures, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), MesasPollingPolicy.DelayAfterFailure(failures));

    [Theory]
    [InlineData(0, 2000)]
    [InlineData(700, 1300)]
    [InlineData(2000, 0)]
    [InlineData(4000, 0)]
    public void SuccessDelayIsMeasuredFromRequestStart(int elapsedMs, int expectedMs) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), MesasPollingPolicy.DelayAfterSuccess(TimeSpan.FromMilliseconds(elapsedMs)));
}
