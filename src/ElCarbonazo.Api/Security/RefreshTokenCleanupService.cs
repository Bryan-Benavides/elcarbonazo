using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Security;

public sealed class RefreshTokenCleanupService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<RefreshTokenCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var cutoff = timeProvider.GetUtcNow().Subtract(SessionSecurityPolicy.RefreshHistoryRetention);
                var deleted = await context.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM token_refresh_sesion AS token
                    USING sesion_usuario AS session
                    WHERE token.id_sesion = session.id_sesion
                      AND COALESCE(
                          session.fecha_revocacion,
                          LEAST(session.expiracion_absoluta, session.ultima_actividad + INTERVAL '15 minutes'))
                          < {cutoff}
                    """, stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("Removed {Count} expired refresh-token history rows", deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Refresh-token history cleanup failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
