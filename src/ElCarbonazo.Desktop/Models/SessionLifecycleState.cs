namespace ElCarbonazo.Desktop.Models;

public enum SessionLifecycleState
{
    SignedOut,
    Active,
    Expiring,
    Paused,
    Expired,
    SecurityBlocked,
    ShuttingDown
}

public sealed class SessionStateChangedEventArgs(
    SessionLifecycleState previousState,
    SessionLifecycleState state,
    string? message = null) : EventArgs
{
    public SessionLifecycleState PreviousState { get; } = previousState;
    public SessionLifecycleState State { get; } = state;
    public string? Message { get; } = message;
}
