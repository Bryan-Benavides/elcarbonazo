namespace ElCarbonazo.Api.Domain;

public static class LoginSecurityPolicy
{
    public const int MaximumFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
}
