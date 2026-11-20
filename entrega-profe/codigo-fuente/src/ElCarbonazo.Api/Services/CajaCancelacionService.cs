using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed record CajaCancelacionResult(int Status, string Code, string? Estado = null, string? ResultadoJson = null);

public sealed class CajaCancelacionService(AppDbContext context, IAuditRecorder audit, TimeProvider time)
{
    public async Task<CajaCancelacionResult> CancelarAsync(Guid id, int actorId, bool confirmaIntervencion, CancellationToken cancellationToken, Guid? sessionId = null)
    {
        if (id == Guid.Empty || actorId <= 0) return new(400, "VALIDATION_ERROR");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await CajaLocks.OperacionAsync(context, id, cancellationToken);
        if (sessionId is not null && !await CajaSesion.VigenteAsync(context, sessionId.Value, actorId, time.GetUtcNow(), cancellationToken)) return new(401, "AUTH_SESION_EXPIRADA");
        var actor = await context.Usuarios.AsNoTracking().SingleOrDefaultAsync(x => x.IdUsuario == actorId && x.Activo, cancellationToken);
        if (actor is null || actor.Rol is not (UserRoles.Jefa or UserRoles.EncargadoCaja)) return new(403, "AUTH_PROHIBIDO");
        var operation = await context.OperacionesCaja.FromSqlInterpolated($"""
            SELECT * FROM operacion_caja WHERE id_operacion = {id} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        if (operation is not null && operation.IdUsuarioPropietario != actorId)
        {
            if (actor.Rol != UserRoles.Jefa) return new(404, "RECURSO_NO_ENCONTRADO");
            if (!confirmaIntervencion) return new(400, "VALIDATION_ERROR");
        }
        if (operation is not null && operation.Estado != CajaStates.OperacionPreparada)
        {
            if (operation.IdUsuarioPropietario != actorId)
            {
                audit.Record("IntervencionOperacionCaja", "Exitoso", actorId, entityType: "OperacionCaja", entityId: id.ToString("D"));
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            return new(operation.StatusResultado ?? 500, operation.CodigoResultado ?? "ERROR", operation.Estado, operation.ResultadoJson);
        }

        var now = time.GetUtcNow();
        if (operation is null)
        {
            operation = new OperacionCaja { IdOperacion = id, IdUsuarioPropietario = actorId, FechaCreacion = now };
            context.OperacionesCaja.Add(operation);
        }
        operation.Estado = CajaStates.OperacionCancelada;
        operation.FechaTerminal = now;
        operation.StatusResultado = 200;
        operation.CodigoResultado = "OPERACION_CANCELADA";
        operation.ResultadoJson = "{\"estado\":\"Cancelada\"}";
        operation.IdUsuarioCancelacion = actorId;
        operation.FechaCancelacion = now;
        audit.Record("OperacionCajaCancelada", "Exitoso", actorId, entityType: "OperacionCaja", entityId: id.ToString("D"));
        if (operation.IdUsuarioPropietario != actorId)
            audit.Record("IntervencionOperacionCaja", "Exitoso", actorId, entityType: "OperacionCaja", entityId: id.ToString("D"));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(200, operation.CodigoResultado, operation.Estado, operation.ResultadoJson);
    }
}
