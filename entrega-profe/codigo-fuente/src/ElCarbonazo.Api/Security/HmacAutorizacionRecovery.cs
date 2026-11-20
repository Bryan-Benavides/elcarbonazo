using ElCarbonazo.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Security;

public enum ResultadoRecoveryHmac { RecuperadoEnEspera, ClaveInvalida, EstadoExistente }

/// <summary>Recuperación administrativa explícita exclusivamente de metadatos ausentes.</summary>
public sealed class HmacAutorizacionRecovery(AppDbContext context, IAuditRecorder audit, TimeProvider time)
{
    public async Task<ResultadoRecoveryHmac> RecuperarAusenteAsync(byte[]? key, CancellationToken cancellationToken)
    {
        if (key is null || key.Length < 32) return ResultadoRecoveryHmac.ClaveInvalida;
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("La recuperación HMAC debe administrar su propia transacción.");

        var fingerprint = HmacAutorizacionGuard.CalcularHuella(key);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await HmacAutorizacionLock.AdquirirAsync(context, cancellationToken);
        var rows = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO estado_hmac_autorizacion (id, provisionado, huella, espera_hasta_utc)
            VALUES (1, true, {fingerprint}, {time.GetUtcNow().AddHours(24)})
            ON CONFLICT (id) DO NOTHING
            """, cancellationToken);
        if (rows != 1) return ResultadoRecoveryHmac.EstadoExistente;

        audit.Record("HmacAutorizacionRecuperado", "Exitoso", entityType: "seguridad", entityId: "hmac-autorizacion",
            details: new Dictionary<string, object?> { ["esperaHoras"] = 24 });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ResultadoRecoveryHmac.RecuperadoEnEspera;
    }
}
