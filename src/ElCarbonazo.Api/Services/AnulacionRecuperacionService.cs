using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed record AnulacionRecuperacionResult(
    int Status, string Code, string? Estado = null, string? ResultadoJson = null, bool Resuelta = false);

// Not exposed over HTTP until the authorization and execution paths use the same barrier.
public sealed class AnulacionRecuperacionService(AppDbContext context, IAuditRecorder audit, TimeProvider time)
{
    public async Task<AnulacionRecuperacionResult> ResolverAsync(
        Guid id, int actorId, Guid sessionId, bool establecerBarrera, bool intervencionAjena, CancellationToken ct)
    {
        if (id == Guid.Empty || actorId <= 0 || sessionId == Guid.Empty) return new(400, "VALIDATION_ERROR");
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        await AnulacionOperacionLock.AdquirirAsync(context, id, ct);
        if (!await CajaSesion.VigenteAsync(context, sessionId, actorId, time.GetUtcNow(), ct))
            return new(401, "AUTH_SESION_EXPIRADA");
        var actor = await context.Usuarios.AsNoTracking().SingleOrDefaultAsync(x => x.IdUsuario == actorId && x.Activo, ct);
        if (actor is null || actor.Rol is not (UserRoles.Jefa or UserRoles.Supervisor or UserRoles.Mesero or UserRoles.EncargadoCaja))
            return new(403, "AUTH_PROHIBIDO");

        var operation = await context.OperacionesAnulacion.FromSqlInterpolated(
            $"SELECT * FROM operacion_anulacion WHERE id_operacion = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (!await CajaSesion.VigenteAsync(context, sessionId, actorId, time.GetUtcNow(), ct))
            return new(401, "AUTH_SESION_EXPIRADA");
        actor = await context.Usuarios.AsNoTracking().SingleOrDefaultAsync(x => x.IdUsuario == actorId && x.Activo, ct);
        if (actor is null || actor.Rol is not (UserRoles.Jefa or UserRoles.Supervisor or UserRoles.Mesero or UserRoles.EncargadoCaja))
            return new(403, "AUTH_PROHIBIDO");
        var ajena = operation is not null && operation.IdUsuarioPropietario != actorId;
        if (ajena && (actor.Rol != UserRoles.Jefa || !intervencionAjena))
            return new(404, "RECURSO_NO_ENCONTRADO");
        if (operation is null && !establecerBarrera) return new(404, "RECURSO_NO_ENCONTRADO");

        if (establecerBarrera && (operation is null || operation.Estado == "Autorizada"))
        {
            var now = time.GetUtcNow();
            if (operation is null)
            {
                operation = new OperacionAnulacion
                {
                    IdOperacion = id,
                    IdUsuarioPropietario = actorId,
                    FechaCreacionUtc = now
                };
                context.OperacionesAnulacion.Add(operation);
            }
            operation.Estado = "Cancelada";
            operation.FechaTerminalUtc = now;
            operation.StatusResultado = 200;
            operation.CodigoResultado = "OPERACION_CANCELADA";
            operation.ResultadoJson = "{}";
            audit.Record("BarreraOperacionAnulacion", "Exitoso", actorId, sessionId, "OperacionAnulacion", id.ToString("D"));
        }
        if (ajena)
            audit.Record("IntervencionOperacionAnulacion", "Exitoso", actorId, sessionId, "OperacionAnulacion", id.ToString("D"));
        await context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(operation!.StatusResultado ?? 200, operation.CodigoResultado ?? "OPERACION_AUTORIZADA",
            operation.Estado, operation.ResultadoJson, Resuelta: true);
    }
}
