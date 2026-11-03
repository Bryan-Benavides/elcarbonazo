using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed class AuthService(
    AppDbContext context,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator tokenGenerator,
    TimeProvider timeProvider,
    IAuditRecorder auditRecorder) : IAuthService
{
    public async Task<LoginResponse?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var attemptedName = string.IsNullOrWhiteSpace(request.Nombre)
            ? null
            : UserIdentity.NormalizeName(request.Nombre);

        if (string.IsNullOrWhiteSpace(request.Contrasena))
        {
            passwordHasher.VerifyDummy(request.Contrasena ?? string.Empty);
            await RecordRejectedLoginAsync(attemptedName, null, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Nombre))
        {
            passwordHasher.VerifyDummy(request.Contrasena);
            await RecordRejectedLoginAsync(attemptedName, null, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var normalizedName = attemptedName!;
        if (!UserIdentity.IsValidName(normalizedName))
        {
            passwordHasher.VerifyDummy(request.Contrasena);
            await RecordRejectedLoginAsync(normalizedName, null, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var usuario = await context.Usuarios
            .FromSqlInterpolated($"""
                SELECT *
                FROM usuario
                WHERE nombre = {normalizedName}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        if (usuario is null ||
            !usuario.Activo ||
            !UserRoles.All.Contains(usuario.Rol) ||
            usuario.BloqueoHasta > now)
        {
            passwordHasher.VerifyDummy(request.Contrasena);
            await RecordRejectedLoginAsync(normalizedName, usuario?.IdUsuario, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (usuario.BloqueoHasta is not null)
        {
            usuario.IntentosFallidos = 0;
            usuario.BloqueoHasta = null;
        }

        if (!passwordHasher.Verify(request.Contrasena, usuario.ContrasenaHash))
        {
            usuario.IntentosFallidos++;
            var lockoutActivated = false;
            if (usuario.IntentosFallidos >= LoginSecurityPolicy.MaximumFailedAttempts)
            {
                usuario.IntentosFallidos = LoginSecurityPolicy.MaximumFailedAttempts;
                usuario.BloqueoHasta = now.Add(LoginSecurityPolicy.LockoutDuration);
                lockoutActivated = true;
            }

            auditRecorder.Record("LoginRechazado", "Rechazado", usuario.IdUsuario);
            if (lockoutActivated)
            {
                auditRecorder.Record(
                    "BloqueoUsuarioActivado",
                    "Exitoso",
                    usuario.IdUsuario,
                    entityType: "usuario",
                    entityId: usuario.IdUsuario.ToString(),
                    details: new Dictionary<string, object?>
                    {
                        ["duracionSegundos"] = (int)LoginSecurityPolicy.LockoutDuration.TotalSeconds
                    });
            }
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        usuario.IntentosFallidos = 0;
        usuario.BloqueoHasta = null;
        if (passwordHasher.NeedsRehash(usuario.ContrasenaHash))
        {
            usuario.ContrasenaHash = passwordHasher.Hash(request.Contrasena);
        }

        var previousSessionIds = await context.SesionesUsuario
            .Where(session => session.IdUsuario == usuario.IdUsuario && session.FechaRevocacion == null)
            .Select(session => session.IdSesion)
            .ToListAsync(cancellationToken);
        await context.SesionesUsuario
            .Where(session => session.IdUsuario == usuario.IdUsuario && session.FechaRevocacion == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(session => session.FechaRevocacion, now),
                cancellationToken);
        foreach (var previousSessionId in previousSessionIds)
        {
            auditRecorder.Record(
                "SesionRevocadaPorNuevoLogin",
                "Exitoso",
                usuario.IdUsuario,
                previousSessionId,
                "sesion_usuario",
                previousSessionId.ToString());
        }

        var sessionId = Guid.NewGuid();
        var session = new SesionUsuario
        {
            IdSesion = sessionId,
            IdUsuario = usuario.IdUsuario,
            FechaCreacion = now,
            UltimaActividad = now,
            ExpiracionAbsoluta = now.Add(SessionSecurityPolicy.AbsoluteLifetime)
        };
        var refreshToken = RefreshTokenCodec.Create();
        session.TokensRefresh.Add(new TokenRefreshSesion
        {
            IdSesion = sessionId,
            TokenHash = RefreshTokenCodec.Hash(refreshToken),
            FechaEmision = now
        });
        context.SesionesUsuario.Add(session);
        auditRecorder.Record(
            "LoginExitoso",
            "Exitoso",
            usuario.IdUsuario,
            sessionId,
            "sesion_usuario",
            sessionId.ToString());

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var accessToken = tokenGenerator.Generate(usuario, sessionId);
        return new LoginResponse(
            accessToken,
            refreshToken,
            now.Add(SessionSecurityPolicy.AccessTokenLifetime),
            now.Add(SessionSecurityPolicy.InactivityLifetime),
            session.ExpiracionAbsoluta,
            now,
            usuario.IdUsuario,
            usuario.Nombre,
            usuario.Rol);
    }

    public async Task<AuthOperationResult> RefreshAsync(
        RefreshRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await using var invalidTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
            auditRecorder.Record("RefreshRechazado", "Rechazado");
            await context.SaveChangesAsync(cancellationToken);
            await invalidTransaction.CommitAsync(cancellationToken);
            return new AuthOperationResult(AuthOperationOutcome.InvalidRefreshToken);
        }

        var tokenHash = RefreshTokenCodec.Hash(request.RefreshToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var storedToken = await context.TokensRefreshSesion
            .FromSqlInterpolated($"""
                SELECT *
                FROM token_refresh_sesion
                WHERE token_hash = {tokenHash}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (storedToken is null)
        {
            auditRecorder.Record("RefreshRechazado", "Rechazado");
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AuthOperationResult(AuthOperationOutcome.InvalidRefreshToken);
        }

        var session = await context.SesionesUsuario
            .FromSqlInterpolated($"""
                SELECT *
                FROM sesion_usuario
                WHERE id_sesion = {storedToken.IdSesion}
                FOR UPDATE
                """)
            .SingleAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        if (storedToken.FechaConsumo is not null)
        {
            session.FechaRevocacion ??= now;
            auditRecorder.Record(
                "RefreshReutilizado",
                "Rechazado",
                session.IdUsuario,
                session.IdSesion,
                "sesion_usuario",
                session.IdSesion.ToString());
            auditRecorder.Record(
                "SesionRevocadaPorReusoRefresh",
                "Exitoso",
                session.IdUsuario,
                session.IdSesion,
                "sesion_usuario",
                session.IdSesion.ToString());
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AuthOperationResult(AuthOperationOutcome.InvalidRefreshToken);
        }

        if (IsExpired(session, now))
        {
            session.FechaRevocacion ??= now;
            auditRecorder.Record(
                "SesionRevocadaPorExpiracion",
                "Exitoso",
                session.IdUsuario,
                session.IdSesion,
                "sesion_usuario",
                session.IdSesion.ToString());
            auditRecorder.Record(
                "RefreshRechazado",
                "Rechazado",
                session.IdUsuario,
                session.IdSesion,
                "sesion_usuario",
                session.IdSesion.ToString());
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AuthOperationResult(AuthOperationOutcome.SessionExpired);
        }

        storedToken.FechaConsumo = now;
        session.UltimaActividad = now;
        var rotatedRefreshToken = RefreshTokenCodec.Create();
        context.TokensRefreshSesion.Add(new TokenRefreshSesion
        {
            IdSesion = session.IdSesion,
            TokenHash = RefreshTokenCodec.Hash(rotatedRefreshToken),
            FechaEmision = now
        });
        auditRecorder.Record("RefreshExitoso", "Exitoso", session.IdUsuario, session.IdSesion);
        var usuario = await context.Usuarios
            .AsNoTracking()
            .SingleAsync(user => user.IdUsuario == session.IdUsuario, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var accessToken = tokenGenerator.Generate(usuario, session.IdSesion);
        return new AuthOperationResult(
            AuthOperationOutcome.Success,
            Tokens: new AuthTokenResponse(
                accessToken,
                rotatedRefreshToken,
                now.Add(SessionSecurityPolicy.AccessTokenLifetime),
                session.UltimaActividad.Add(SessionSecurityPolicy.InactivityLifetime),
                session.ExpiracionAbsoluta,
                now,
                usuario.IdUsuario,
                usuario.Nombre,
                usuario.Rol));
    }

    public async Task<AuthOperationResult> RecordActivityAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var session = await context.SesionesUsuario
            .FromSqlInterpolated($"""
                SELECT *
                FROM sesion_usuario
                WHERE id_sesion = {sessionId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (session is null)
        {
            return new AuthOperationResult(AuthOperationOutcome.SessionExpired);
        }

        var now = timeProvider.GetUtcNow();
        if (IsExpired(session, now))
        {
            session.FechaRevocacion ??= now;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AuthOperationResult(AuthOperationOutcome.SessionExpired);
        }

        if (now - session.UltimaActividad >= SessionSecurityPolicy.ActivityWriteInterval)
        {
            session.UltimaActividad = now;
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new AuthOperationResult(
            AuthOperationOutcome.Success,
            Activity: new ActivityResponse(
                session.UltimaActividad.Add(SessionSecurityPolicy.InactivityLifetime),
                session.ExpiracionAbsoluta,
                now));
    }

    public async Task LogoutAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var session = await context.SesionesUsuario
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.IdSesion == sessionId, cancellationToken);
        var updated = await context.SesionesUsuario
            .Where(session => session.IdSesion == sessionId && session.FechaRevocacion == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(session => session.FechaRevocacion, now),
                cancellationToken);
        if (updated > 0)
        {
            auditRecorder.Record(
                "Logout",
                "Exitoso",
                session?.IdUsuario,
                sessionId,
                "sesion_usuario",
                sessionId.ToString());
            await context.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RecordRejectedLoginAsync(
        string? attemptedName,
        int? userId,
        CancellationToken cancellationToken)
    {
        auditRecorder.Record(
            "LoginRechazado",
            "Rechazado",
            userId,
            details: userId is null && attemptedName is not null
                ? new Dictionary<string, object?> { ["nombreIntentado"] = attemptedName }
                : null);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static bool IsExpired(SesionUsuario session, DateTimeOffset now) =>
        session.FechaRevocacion is not null ||
        now >= session.ExpiracionAbsoluta ||
        now >= session.UltimaActividad.Add(SessionSecurityPolicy.InactivityLifetime);
}
