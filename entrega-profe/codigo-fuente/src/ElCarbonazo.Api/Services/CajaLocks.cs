using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ElCarbonazo.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

internal static class CajaLocks
{
    public static Task OperacionAsync(AppDbContext context, Guid id, CancellationToken cancellationToken)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"elcarbonazo:caja:operacion:v1:{id:D}"));
        var key = BinaryPrimitives.ReadInt64BigEndian(digest);
        return context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
    }

    // Reserved namespace; all future shift opening/closing must use this same key.
    public static Task CajaCompartidaAsync(AppDbContext context, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(40040001)", cancellationToken);
}
