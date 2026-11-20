using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class MesasRequestCoordinatorTests
{
    [Fact]
    public async Task OldReadFinishesBeforeMutationAndCannotApply()
    {
        var coordinator = Online();
        var oldRead = coordinator.TryBeginRead()!.Value;
        var mutation = coordinator.TryBeginMutationAsync();
        Assert.False(mutation.IsCompleted);
        Assert.False(await coordinator.TryBeginMutationAsync());
        Assert.Null(coordinator.TryBeginRead());
        var applied = false;
        Assert.False(coordinator.CompleteRead(oldRead, true, () => applied = true));
        Assert.True(await mutation);
        coordinator.CompleteMutation(false);
        Assert.False(applied);
        Assert.True(coordinator.CanMutate);
    }

    [Fact]
    public async Task UncertainMutationRequiresSuccessfulReadAndExplicitAcknowledgement()
    {
        var coordinator = Online();
        Assert.True(await coordinator.TryBeginMutationAsync());
        coordinator.CompleteMutation(true, uncertain: true);
        Assert.False(coordinator.CanMutate);
        Assert.False(coordinator.AcknowledgeUncertainty());
        var ticket = coordinator.TryBeginRead()!.Value;
        Assert.True(coordinator.CompleteRead(ticket, true));
        Assert.False(coordinator.CanMutate);
        Assert.True(coordinator.AcknowledgeUncertainty());
        Assert.True(coordinator.CanMutate);
    }

    [Fact]
    public void PausingInvalidatesReadAndRequiresFreshReadAfterResume()
    {
        var coordinator = Online();
        var ticket = coordinator.TryBeginRead()!.Value;
        coordinator.SetSessionActive(false);
        coordinator.SetSessionActive(true);
        Assert.False(coordinator.CompleteRead(ticket, true));
        Assert.False(coordinator.CanMutate);
        var next = coordinator.TryBeginRead()!.Value;
        coordinator.CompleteRead(next, true);
        Assert.True(coordinator.CanMutate);
    }

    [Fact]
    public async Task FailedReadPreventsMutationsAndReadsDoNotOverlap()
    {
        var coordinator = Online();
        var ticket = coordinator.TryBeginRead()!.Value;
        Assert.Null(coordinator.TryBeginRead());
        coordinator.CompleteRead(ticket, false);
        Assert.False(await coordinator.TryBeginMutationAsync());
    }

    [Fact]
    public async Task ResponseAfterSessionEndsCannotApplyOrReenableChanges()
    {
        var coordinator = Online();
        Assert.True(await coordinator.TryBeginMutationAsync());
        coordinator.SetSessionActive(false);
        coordinator.SetSessionActive(true);
        var applied = false;

        coordinator.CompleteMutation(false, apply: () => applied = true);

        Assert.False(applied);
        Assert.False(coordinator.CanMutate);
    }

    [Fact]
    public async Task FailedPendingReadPreventsWaitingMutation()
    {
        var coordinator = Online();
        var read = coordinator.TryBeginRead()!.Value;
        var mutation = coordinator.TryBeginMutationAsync();
        coordinator.CompleteRead(read, false);
        Assert.False(await mutation);
        Assert.False(coordinator.CanMutate);
    }

    [Fact]
    public async Task SessionChangeWhileWaitingPreventsMutation()
    {
        var coordinator = Online();
        var read = coordinator.TryBeginRead()!.Value;
        var mutation = coordinator.TryBeginMutationAsync();
        coordinator.SetSessionActive(false);
        coordinator.CompleteRead(read, true);
        Assert.False(await mutation);
    }

    private static MesasRequestCoordinator Online()
    {
        var coordinator = new MesasRequestCoordinator();
        coordinator.SetSessionActive(true);
        var ticket = coordinator.TryBeginRead()!.Value;
        coordinator.CompleteRead(ticket, true);
        return coordinator;
    }
}
