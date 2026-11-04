using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class MesasRequestCoordinatorTests
{
    [Fact]
    public void OldReadCannotOverwriteMutationAndNoNewReadStartsDuringMutation()
    {
        var coordinator = Online();
        var oldRead = coordinator.TryBeginRead()!.Value;
        Assert.True(coordinator.TryBeginMutation());
        Assert.False(coordinator.TryBeginMutation());
        Assert.Null(coordinator.TryBeginRead());
        var applied = false;
        Assert.False(coordinator.CompleteRead(oldRead, true, () => applied = true));
        coordinator.CompleteMutation(false);
        Assert.False(applied);
        Assert.True(coordinator.CanMutate);
    }

    [Fact]
    public void UncertainMutationRequiresSuccessfulReadAndExplicitAcknowledgement()
    {
        var coordinator = Online();
        Assert.True(coordinator.TryBeginMutation());
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
    public void FailedReadPreventsMutationsAndReadsDoNotOverlap()
    {
        var coordinator = Online();
        var ticket = coordinator.TryBeginRead()!.Value;
        Assert.Null(coordinator.TryBeginRead());
        coordinator.CompleteRead(ticket, false);
        Assert.False(coordinator.TryBeginMutation());
    }

    [Fact]
    public void ResponseAfterSessionEndsCannotApplyOrReenableChanges()
    {
        var coordinator = Online();
        Assert.True(coordinator.TryBeginMutation());
        coordinator.SetSessionActive(false);
        coordinator.SetSessionActive(true);
        var applied = false;

        coordinator.CompleteMutation(false, apply: () => applied = true);

        Assert.False(applied);
        Assert.False(coordinator.CanMutate);
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
