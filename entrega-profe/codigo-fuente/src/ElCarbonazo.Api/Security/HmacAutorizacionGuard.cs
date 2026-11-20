using System.Security.Cryptography;
using System.Text;
using ElCarbonazo.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ElCarbonazo.Api.Security;

public enum EstadoHmacAutorizacion { Disponible, ClaveInvalida, NoProvisionado, MetadatosAusentes, Espera }

/// <summary>
/// Serializa la comprobación y el trabajo autorizado en la misma transacción.
/// No aprovisiona claves ni concede permisos por sí mismo.
/// </summary>
public sealed class HmacAutorizacionGuard(AppDbContext context, TimeProvider time)
{
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes("elcarbonazo:autorizacion-hmac:huella:v1");

    public static string CalcularHuella(ReadOnlySpan<byte> key)
    {
        if (key.Length < 32) throw new ArgumentException("Clave HMAC inválida.", nameof(key));
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Domain));
    }

    public async Task<EstadoHmacAutorizacion> EjecutarAsync(
        byte[]? key, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (key is null || key.Length < 32) return EstadoHmacAutorizacion.ClaveInvalida;
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("La protección HMAC debe administrar su propia transacción.");

        var fingerprint = CalcularHuella(key);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await HmacAutorizacionLock.AdquirirAsync(context, cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT provisionado, huella, espera_hasta_utc FROM estado_hmac_autorizacion WHERE id = 1 FOR UPDATE";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return EstadoHmacAutorizacion.MetadatosAusentes;
        var provisioned = reader.GetBoolean(0);
        var stored = reader.IsDBNull(1) ? null : reader.GetString(1);
        var until = reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(2);
        await reader.CloseAsync();
        if (!provisioned || stored is null) return EstadoHmacAutorizacion.NoProvisionado;

        var now = time.GetUtcNow();
        if (!string.Equals(stored, fingerprint, StringComparison.Ordinal))
        {
            var next = now.AddHours(24);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE estado_hmac_autorizacion SET huella = {fingerprint}, espera_hasta_utc = {next} WHERE id = 1
                """, cancellationToken);
            // El cambio debe ser durable incluso cuando se rechaza esta autorización.
            await transaction.CommitAsync(cancellationToken);
            return EstadoHmacAutorizacion.Espera;
        }
        if (until is not null && now < until)
            return EstadoHmacAutorizacion.Espera;

        await work(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return EstadoHmacAutorizacion.Disponible;
    }
}
