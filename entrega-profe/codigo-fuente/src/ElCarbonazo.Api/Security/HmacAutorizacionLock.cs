using ElCarbonazo.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Security;

internal static class HmacAutorizacionLock
{
    // Esta barrera también protege la ausencia de la fila singleton.
    public static Task AdquirirAsync(AppDbContext context, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7329810060001)", cancellationToken);
}
