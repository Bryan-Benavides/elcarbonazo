using ElCarbonazo.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ElCarbonazo.Api.Security;

public enum ResultadoProvisionHmac { Provisionado, ClaveInvalida, MetadatosAusentes, YaProvisionado, EvidenciaExistente }

/// <summary>
/// Primera provisión explícita con conexión administrativa; nunca repara metadatos ausentes.
/// No se invoca durante el arranque ni expone un endpoint.
/// </summary>
public sealed class HmacAutorizacionProvisioner(AppDbContext context, IAuditRecorder audit)
{
    public async Task<ResultadoProvisionHmac> ProvisionarInicialAsync(byte[]? key, CancellationToken cancellationToken)
    {
        if (key is null || key.Length < 32) return ResultadoProvisionHmac.ClaveInvalida;
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("La provisión HMAC debe administrar su propia transacción.");

        var fingerprint = HmacAutorizacionGuard.CalcularHuella(key);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await HmacAutorizacionLock.AdquirirAsync(context, cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT provisionado FROM estado_hmac_autorizacion WHERE id = 1 FOR UPDATE";
        var provisioned = await command.ExecuteScalarAsync(cancellationToken);
        if (provisioned is null) return ResultadoProvisionHmac.MetadatosAusentes;
        if ((bool)provisioned) return ResultadoProvisionHmac.YaProvisionado;

        if (await context.OperacionesAnulacion.AnyAsync(cancellationToken) ||
            await context.AutorizacionesAnulacion.AnyAsync(cancellationToken) ||
            await context.LimitesAutorizacion.AnyAsync(cancellationToken))
            return ResultadoProvisionHmac.EvidenciaExistente;

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE estado_hmac_autorizacion SET provisionado = true, huella = {fingerprint}
            WHERE id = 1 AND NOT provisionado
            """, cancellationToken);
        audit.Record("HmacAutorizacionProvisionado", "Exitoso", entityType: "seguridad", entityId: "hmac-autorizacion");
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ResultadoProvisionHmac.Provisionado;
    }
}
