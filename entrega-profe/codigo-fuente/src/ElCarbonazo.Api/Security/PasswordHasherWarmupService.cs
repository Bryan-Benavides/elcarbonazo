namespace ElCarbonazo.Api.Security;

public sealed class PasswordHasherWarmupService(IPasswordHasher passwordHasher) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = passwordHasher;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
