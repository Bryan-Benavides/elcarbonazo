using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

internal static class CajaSesion
{
    public static Task<bool> VigenteAsync(AppDbContext context, Guid id, int actorId, DateTimeOffset now, CancellationToken ct) =>
        context.SesionesUsuario.AsNoTracking().AnyAsync(x => x.IdSesion == id && x.IdUsuario == actorId &&
            x.FechaRevocacion == null && x.ExpiracionAbsoluta > now &&
            x.UltimaActividad > now.Subtract(SessionSecurityPolicy.InactivityLifetime), ct);
}
