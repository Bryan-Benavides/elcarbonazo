using System.Security.Cryptography;
using ElCarbonazo.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Security;

/// <summary>Comando administrativo explícito; no genera ni imprime secretos.</summary>
public static class HmacProvisionCommand
{
    public static async Task<int> RunAsync(
        AppDbContext context, IAuditRecorder audit, string? hmacBase64, string? jwtBase64,
        TextReader input, TextWriter output, CancellationToken cancellationToken, bool recuperar = false)
    {
        byte[]? key = null;
        byte[]? jwt = null;
        try
        {
            if (string.IsNullOrWhiteSpace(hmacBase64) || string.IsNullOrWhiteSpace(jwtBase64))
                throw new InvalidOperationException();
            key = Convert.FromBase64String(hmacBase64);
            jwt = Convert.FromBase64String(jwtBase64);
            if (key.Length < 32 || jwt.Length < 32 ||
                (key.Length == jwt.Length && CryptographicOperations.FixedTimeEquals(key, jwt)))
                throw new InvalidOperationException();

            await context.Database.OpenConnectionAsync(cancellationToken);
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT current_database(), COALESCE(inet_server_addr()::text, 'local'), inet_server_port()";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(2))
                throw new InvalidOperationException();
            var destination = $"{reader.GetString(0)}@{reader.GetString(1)}:{reader.GetInt32(2)}";
            await reader.CloseAsync();
            var action = recuperar ? "RECUPERAR" : "APROVISIONAR";
            await output.WriteLineAsync($"Destino PostgreSQL efectivo: {destination}");
            if (recuperar)
                await output.WriteLineAsync("La recuperación exige 24 horas de espera y solo admite metadatos ausentes.");
            await output.WriteLineAsync($"Para confirmar escriba exactamente: {action} {destination}");
            var confirmation = await input.ReadLineAsync(cancellationToken);
            if (!string.Equals(confirmation, $"{action} {destination}", StringComparison.Ordinal))
            {
                await output.WriteLineAsync("Provisión cancelada; no se realizaron cambios.");
                return 1;
            }

            if (recuperar)
            {
                var recovered = await new HmacAutorizacionRecovery(context, audit, TimeProvider.System)
                    .RecuperarAusenteAsync(key, cancellationToken);
                await output.WriteLineAsync(recovered == ResultadoRecoveryHmac.RecuperadoEnEspera
                    ? "Recuperación registrada; autorizaciones bloqueadas durante 24 horas."
                    : "Recuperación rechazada; conserve el estado existente y revise el procedimiento.");
                return recovered == ResultadoRecoveryHmac.RecuperadoEnEspera ? 0 : 1;
            }
            else
            {
                var result = await new HmacAutorizacionProvisioner(context, audit).ProvisionarInicialAsync(key, cancellationToken);
                await output.WriteLineAsync(result == ResultadoProvisionHmac.Provisionado
                    ? "Primera provisión completada."
                    : "Provisión rechazada; conserve el estado existente y revise el procedimiento.");
                return result == ResultadoProvisionHmac.Provisionado ? 0 : 1;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await output.WriteLineAsync("Provisión no completada; verifique configuración, destino y permisos sin exponer secretos.");
            return 1;
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (jwt is not null) CryptographicOperations.ZeroMemory(jwt);
        }
    }
}
