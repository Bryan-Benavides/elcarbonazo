using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed record AnulacionAutorizacionResult(int Status, string Code, AutorizarAnulacionResponse? Data = null);

public sealed class AnulacionAutorizacionService(
    AppDbContext context, IPasswordHasher passwords, IAuditRecorder audit, TimeProvider time, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AnulacionAutorizacionResult> AutorizarAsync(
        AutorizarAnulacionRequest request, int actorId, Guid sessionId, CancellationToken ct)
    {
        var motive = NormalizeReason(request.Motivo);
        var email = request.EmailJefa?.Trim().ToLowerInvariant();
        if (request.IdOperacion == Guid.Empty || actorId <= 0 || sessionId == Guid.Empty ||
            request.IdPedido <= 0 || request.IdComanda <= 0 || request.IdDetalle is <= 0 ||
            motive is null || string.IsNullOrWhiteSpace(email) || email.Length > 254 ||
            string.IsNullOrEmpty(request.Contrasena) || request.Contrasena.Length > 256)
            return new(400, "VALIDATION_ERROR");

        byte[]? key;
        try { key = Convert.FromBase64String(configuration["AutorizacionSuperior:ClaveHmac"] ?? string.Empty); }
        catch (FormatException) { key = null; }
        if (key is not { Length: >= 32 }) return new(403, "AUTORIZACION_SUPERIOR_INVALIDA");

        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            actorId,
            request.IdOperacion,
            request.IdPedido,
            request.IdComanda,
            request.IdDetalle,
            motivo = motive
        }, JsonOptions);
        var intentionHash = Convert.ToHexStringLower(SHA256.HashData(canonical));
        var pairHash = Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(email)));
        var pairLockBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"elcarbonazo:autorizacion-par:v1:{actorId}:{pairHash}"));
        var pairLock = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(pairLockBytes);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var expiry = time.GetUtcNow().AddSeconds(120);
        var outcome = new AnulacionAutorizacionResult(403, "AUTORIZACION_SUPERIOR_INVALIDA");

        try
        {
            var guard = new HmacAutorizacionGuard(context, time);
            var state = await guard.EjecutarAsync(key, async tokenCt =>
            {
                await AnulacionOperacionLock.AdquirirAsync(context, request.IdOperacion, tokenCt);
                await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({pairLock})", tokenCt);
                if (!await CajaSesion.VigenteAsync(context, sessionId, actorId, time.GetUtcNow(), tokenCt))
                {
                    outcome = new(401, "AUTH_SESION_EXPIRADA");
                    return;
                }
                var requester = await context.Usuarios.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.IdUsuario == actorId && x.Activo, tokenCt);
                if (requester is null || requester.Rol is not (UserRoles.Mesero or UserRoles.EncargadoCaja or UserRoles.Jefa or UserRoles.Supervisor))
                {
                    outcome = new(403, "AUTH_PROHIBIDO");
                    return;
                }

                var existing = await context.OperacionesAnulacion.FromSqlInterpolated(
                    $"SELECT * FROM operacion_anulacion WHERE id_operacion = {request.IdOperacion} FOR UPDATE").SingleOrDefaultAsync(tokenCt);
                if (existing is not null && existing.Estado != "Autorizada")
                {
                    outcome = new(409, "OPERACION_TERMINAL");
                    return;
                }
                if (existing is not null && (existing.IdUsuarioPropietario != actorId || existing.HashIntencion != intentionHash))
                {
                    outcome = existing.IdUsuarioPropietario != actorId
                        ? new(404, "RECURSO_NO_ENCONTRADO")
                        : new(409, "OPERACION_CLAVE_EN_USO");
                    return;
                }

                var authorizer = await context.Usuarios.SingleOrDefaultAsync(
                    x => x.Email == email && x.Rol == UserRoles.Jefa && x.Activo, tokenCt);
                LimiteAutorizacion? limit = await context.LimitesAutorizacion.FromSqlInterpolated($"""
                SELECT * FROM limite_autorizacion
                WHERE id_usuario_solicita = {actorId} AND hash_nombre = {pairHash}
                FOR UPDATE
                """).SingleOrDefaultAsync(tokenCt);
                var now = time.GetUtcNow();
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                WITH vencidos AS (
                    SELECT ctid FROM limite_autorizacion
                    WHERE id_usuario_solicita = {actorId}
                      AND hash_nombre <> {pairHash}
                      AND ultima_actividad_utc < {now.AddHours(-24)}
                      AND (bloqueo_hasta_utc IS NULL OR bloqueo_hasta_utc <= {now})
                    ORDER BY ultima_actividad_utc
                    LIMIT 100
                    FOR UPDATE SKIP LOCKED
                )
                DELETE FROM limite_autorizacion l USING vencidos v WHERE l.ctid = v.ctid
                """, tokenCt);
                var pairCount = await context.LimitesAutorizacion.CountAsync(x => x.IdUsuarioSolicita == actorId, tokenCt);
                if (limit is null && pairCount >= 100)
                {
                    audit.Record("AutorizacionSuperiorIntento", "Rechazado", actorId, sessionId,
                        details: new Dictionary<string, object?> { ["motivo"] = "LimitePares" });
                    await context.SaveChangesAsync(tokenCt);
                    outcome = new(429, "DEMASIADOS_INTENTOS_AUTORIZACION");
                    return;
                }
                var blocked = limit?.BloqueoHastaUtc is DateTimeOffset until && now < until;
                var passwordValid = authorizer is not null
                    ? passwords.Verify(request.Contrasena, authorizer.ContrasenaHash)
                    : VerifyDummy(request.Contrasena);
                if (!passwordValid || authorizer is null || blocked)
                {
                    if (!blocked)
                    {
                        if (limit is null)
                        {
                            limit = new LimiteAutorizacion { IdUsuarioSolicita = actorId, HashNombre = pairHash, UltimaActividadUtc = now };
                            context.LimitesAutorizacion.Add(limit);
                        }
                        limit.IntentosFallidos = Math.Min(5, limit.IntentosFallidos + 1);
                        limit.UltimaActividadUtc = now;
                        if (limit.IntentosFallidos >= 5) limit.BloqueoHastaUtc = now.AddMinutes(5);
                    }
                    audit.Record("AutorizacionSuperiorIntento", "Rechazado", actorId, sessionId,
                        details: new Dictionary<string, object?> { ["intentos"] = limit?.IntentosFallidos ?? 0 });
                    await context.SaveChangesAsync(tokenCt);
                    outcome = blocked ? new(429, "DEMASIADOS_INTENTOS_AUTORIZACION") : new(403, "AUTORIZACION_SUPERIOR_INVALIDA");
                    return;
                }

                if (limit is not null)
                {
                    limit.IntentosFallidos = 0;
                    limit.BloqueoHastaUtc = null;
                    limit.UltimaActividadUtc = now;
                }
                if (existing is null)
                {
                    existing = new OperacionAnulacion
                    {
                        IdOperacion = request.IdOperacion,
                        IdUsuarioPropietario = actorId,
                        HashIntencion = intentionHash,
                        Estado = "Autorizada",
                        FechaCreacionUtc = now
                    };
                    context.OperacionesAnulacion.Add(existing);
                }
                context.AutorizacionesAnulacion.Add(new AutorizacionAnulacion
                {
                    IdOperacion = request.IdOperacion,
                    IdSesion = sessionId,
                    IdUsuarioAutoriza = authorizer!.IdUsuario,
                    HashToken = tokenHash,
                    FechaEmisionUtc = now,
                    FechaExpiracionUtc = expiry
                });
                audit.Record("AutorizacionSuperiorEmitida", "Exitoso", actorId, sessionId, "OperacionAnulacion", request.IdOperacion.ToString("D"));
                await context.SaveChangesAsync(tokenCt);
                outcome = new(200, "OK", new(token, expiry));
            }, ct);
            return state == EstadoHmacAutorizacion.Disponible
                ? outcome
                : new(state == EstadoHmacAutorizacion.Espera ? 403 : 503,
                    state == EstadoHmacAutorizacion.Espera ? "AUTORIZACION_SUPERIOR_INVALIDA" : "AUTORIZACION_SUPERIOR_NO_DISPONIBLE");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private bool VerifyDummy(string password)
    {
        passwords.VerifyDummy(password);
        return false;
    }

    private static string? NormalizeReason(string? reason)
    {
        if (reason is null) return null;
        try { reason = reason.Normalize(NormalizationForm.FormC).Trim(); }
        catch (ArgumentException) { return null; }
        return reason.EnumerateRunes().Count() is >= 5 and <= 250 &&
            !reason.EnumerateRunes().Any(Rune.IsControl) ? reason : null;
    }
}
