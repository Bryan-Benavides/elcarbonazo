using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

// Only types supported by a registered domain handler can execute.
public sealed record CajaEjecucionPreparada(Guid IdOperacion, int IdUsuario, string Tipo, short Version, string ArgumentosJson);
public sealed record CajaResultadoDominio(bool Exitoso, short Status, string Codigo, string ResultadoJson);

// Implementations must use the supplied context and its existing transaction; never commit independently.
public interface ICajaEjecutorDominio
{
    bool PuedeEjecutar(string tipo) => true;
    Task<CajaResultadoDominio> EjecutarAsync(CajaEjecucionPreparada operacion, CancellationToken cancellationToken);
}

public sealed class CajaOperacionesService(AppDbContext context, IAuditRecorder audit, TimeProvider time, ICajaEjecutorDominio? ejecutor = null)
{
    public async Task<CajaCancelacionResult> PrepararAsync(Guid id, int actorId, CajaIntencionCanonica? intent, CancellationToken ct, Guid? sessionId = null)
    {
        if (id == Guid.Empty || actorId <= 0 || intent is null) return new(400, "VALIDATION_ERROR");
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        await CajaLocks.OperacionAsync(context, id, ct);
        if (sessionId is not null && !await CajaSesion.VigenteAsync(context, sessionId.Value, actorId, time.GetUtcNow(), ct)) return new(401, "AUTH_SESION_EXPIRADA");
        if (!await ActorAutorizadoAsync(actorId, ct)) return new(403, "AUTH_PROHIBIDO");
        var existing = await context.OperacionesCaja.FromSqlInterpolated($"SELECT * FROM operacion_caja WHERE id_operacion = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (existing is not null)
        {
            if (existing.IdUsuarioPropietario != actorId) return new(404, "RECURSO_NO_ENCONTRADO");
            if (existing.Tipo != intent.Tipo || existing.VersionContrato != intent.Version || existing.HashArgumentos != intent.HashArgumentos)
                return new(409, "OPERACION_CLAVE_EN_USO");
            return Respuesta(existing);
        }
        // Serialize new intentions with opening/closing before they inspect nonterminal operations.
        await CajaLocks.CajaCompartidaAsync(context, ct);
        if (sessionId is not null && !await CajaSesion.VigenteAsync(context, sessionId.Value, actorId, time.GetUtcNow(), ct)) return new(401, "AUTH_SESION_EXPIRADA");
        if (!await ActorAutorizadoAsync(actorId, ct)) return new(403, "AUTH_PROHIBIDO");
        if (intent.Tipo == "CobrarCuenta")
        {
            using var json = JsonDocument.Parse(intent.ArgumentosJson);
            var shiftId = json.RootElement.GetProperty("turnoId").GetInt32();
            if (!await context.TurnosCaja.AsNoTracking().AnyAsync(x => x.IdTurnoCaja == shiftId && x.Estado == CajaStates.TurnoAbierto, ct))
                return new(409, "TURNO_NO_ABIERTO");
        }
        var operation = new OperacionCaja
        {
            IdOperacion = id,
            IdUsuarioPropietario = actorId,
            Tipo = intent.Tipo,
            VersionContrato = intent.Version,
            Argumentos = intent.ArgumentosJson,
            HashArgumentos = intent.HashArgumentos,
            Estado = CajaStates.OperacionPreparada,
            FechaCreacion = time.GetUtcNow()
        };
        context.OperacionesCaja.Add(operation);
        await context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Respuesta(operation);
    }

    public async Task<CajaCancelacionResult> ConsultarAsync(Guid id, int actorId, CancellationToken ct, Guid? sessionId = null)
    {
        if (id == Guid.Empty || actorId <= 0) return new(400, "VALIDATION_ERROR");
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        await CajaLocks.OperacionAsync(context, id, ct);
        if (sessionId is not null && !await CajaSesion.VigenteAsync(context, sessionId.Value, actorId, time.GetUtcNow(), ct)) return new(401, "AUTH_SESION_EXPIRADA");
        var actor = await context.Usuarios.AsNoTracking().SingleOrDefaultAsync(x => x.IdUsuario == actorId && x.Activo, ct);
        if (actor is null || actor.Rol is not (UserRoles.Jefa or UserRoles.EncargadoCaja)) return new(403, "AUTH_PROHIBIDO");
        var operation = await context.OperacionesCaja.AsNoTracking().SingleOrDefaultAsync(x => x.IdOperacion == id, ct);
        if (operation is null || (operation.IdUsuarioPropietario != actorId && actor.Rol != UserRoles.Jefa))
            return new(404, "RECURSO_NO_ENCONTRADO");
        if (operation.IdUsuarioPropietario != actorId)
        {
            audit.Record("IntervencionOperacionCaja", "Exitoso", actorId, entityType: "OperacionCaja", entityId: id.ToString("D"));
            await context.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return Respuesta(operation);
    }

    public async Task<CajaCancelacionResult> EjecutarAsync(Guid id, int actorId, CancellationToken ct, Guid? sessionId = null)
    {
        if (id == Guid.Empty || actorId <= 0) return new(400, "VALIDATION_ERROR");
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        await CajaLocks.OperacionAsync(context, id, ct);
        if (sessionId is not null && !await CajaSesion.VigenteAsync(context, sessionId.Value, actorId, time.GetUtcNow(), ct)) return new(401, "AUTH_SESION_EXPIRADA");
        if (!await ActorAutorizadoAsync(actorId, ct)) return new(403, "AUTH_PROHIBIDO");
        var operation = await context.OperacionesCaja.FromSqlInterpolated($"SELECT * FROM operacion_caja WHERE id_operacion = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (operation is null || operation.IdUsuarioPropietario != actorId) return new(404, "RECURSO_NO_ENCONTRADO");
        if (operation.Estado != CajaStates.OperacionPreparada) return Respuesta(operation);
        if (operation.Tipo is null || operation.VersionContrato is null || operation.Argumentos is null)
            throw new InvalidOperationException("La operación preparada carece de contrato válido.");
        if (ejecutor is null || !ejecutor.PuedeEjecutar(operation.Tipo)) return new(503, "OPERACION_EJECUCION_NO_DISPONIBLE", operation.Estado);

        if (operation.Tipo is "AbrirTurno" or "CerrarTurno")
        {
            // Keep the shared lock outside the domain savepoint, including deterministic rejections.
            await CajaLocks.CajaCompartidaAsync(context, ct);
            if (sessionId is not null && !await CajaSesion.VigenteAsync(context, sessionId.Value, actorId, time.GetUtcNow(), ct)) return new(401, "AUTH_SESION_EXPIRADA");
            if (!await ActorAutorizadoAsync(actorId, ct)) return new(403, "AUTH_PROHIBIDO");
        }

        await tx.CreateSavepointAsync("caja_dominio", ct);
        var outcome = await ejecutor.EjecutarAsync(new CajaEjecucionPreparada(
            id, actorId, operation.Tipo, operation.VersionContrato.Value, operation.Argumentos), ct);
        using var json = JsonDocument.Parse(outcome.ResultadoJson);
        if (json.RootElement.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(outcome.Codigo) ||
            outcome.Codigo.Length > 80 || (outcome.Exitoso ? outcome.Status is < 200 or >= 300 : outcome.Status is < 400 or >= 500))
            throw new InvalidOperationException("El ejecutor devolvió un resultado terminal inválido.");
        if (!outcome.Exitoso)
        {
            await tx.RollbackToSavepointAsync("caja_dominio", ct);
            context.ChangeTracker.Clear();
            operation = await context.OperacionesCaja.SingleAsync(x => x.IdOperacion == id, ct);
        }
        operation.Estado = outcome.Exitoso ? CajaStates.OperacionCompletada : CajaStates.OperacionRechazada;
        operation.FechaTerminal = time.GetUtcNow();
        operation.StatusResultado = outcome.Status;
        operation.CodigoResultado = outcome.Codigo;
        operation.ResultadoJson = outcome.ResultadoJson;
        await context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Respuesta(operation);
    }

    private Task<bool> ActorAutorizadoAsync(int id, CancellationToken ct) =>
        context.Usuarios.AsNoTracking().AnyAsync(x => x.IdUsuario == id && x.Activo &&
            (x.Rol == UserRoles.Jefa || x.Rol == UserRoles.EncargadoCaja), ct);

    private static CajaCancelacionResult Respuesta(OperacionCaja operation) => operation.Estado == CajaStates.OperacionPreparada
        ? new(200, "OPERACION_PREPARADA", operation.Estado)
        : new(operation.StatusResultado ?? 500, operation.CodigoResultado ?? "ERROR", operation.Estado, operation.ResultadoJson);
}
