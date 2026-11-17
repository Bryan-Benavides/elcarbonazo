using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ElCarbonazo.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

// All future authorization, execution and recovery paths must use this same lock.
internal static class AnulacionOperacionLock
{
    public static Task AdquirirAsync(AppDbContext context, Guid id, CancellationToken ct)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"elcarbonazo:anulacion:operacion:v1:{id:D}"));
        var key = BinaryPrimitives.ReadInt64BigEndian(digest);
        return context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    }
}
