using System.Data;
using System.Net;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ElCarbonazo.Api.Security;

/// <summary>Preparación explícita de tres cuentas locales de presentación; no es administración general.</summary>
public static class CuentasDefensaCommand
{
    public static async Task<int> RunAsync(AppDbContext db, IPasswordHasher hasher, IAuditRecorder audit,
        bool development, string? jefa, string? cocina, string? mesero, TextReader input, TextWriter output,
        CancellationToken ct)
    {
        try
        {
            var cs = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
            if (!development || cs.Host is not ("localhost" or "127.0.0.1" or "::1") ||
                cs.Database != "elcarbonazo" || string.IsNullOrEmpty(jefa) ||
                string.IsNullOrEmpty(cocina) || string.IsNullOrEmpty(mesero))
                throw new InvalidOperationException();
            await db.Database.OpenConnectionAsync(ct);
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT current_database(), inet_server_addr()::text, inet_server_port()";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.GetString(0) != "elcarbonazo" || reader.IsDBNull(1))
                throw new InvalidOperationException();
            var address = reader.GetString(1).Split('/')[0];
            if (!IPAddress.TryParse(address, out var ip) || !IPAddress.IsLoopback(ip))
                throw new InvalidOperationException();
            var destination = $"elcarbonazo@{address}:{reader.GetInt32(2)}";
            await reader.CloseAsync();
            await output.WriteLineAsync($"Destino PostgreSQL efectivo: {destination}");
            await output.WriteLineAsync($"Para confirmar escriba exactamente: PREPARAR {destination}");
            if (await input.ReadLineAsync(ct) != $"PREPARAR {destination}")
            {
                await output.WriteLineAsync("Cancelado; no se realizaron cambios.");
                return 1;
            }
            await PrepararAsync(db, hasher, audit, jefa, cocina, mesero, ct);
            await output.WriteLineAsync("Cuentas de prueba preparadas.");
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await output.WriteLineAsync("No se confirmó la preparación. No repetir automáticamente; revisar destino y estado.");
            return 1;
        }
    }

    public static async Task PrepararAsync(AppDbContext db, IPasswordHasher hasher, IAuditRecorder audit,
        string jefa, string cocina, string mesero, CancellationToken ct)
    {
        if (new[] { jefa, cocina, mesero }.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Faltan contraseñas.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE usuario IN EXCLUSIVE MODE", ct);
        var users = await db.Usuarios.OrderBy(x => x.IdUsuario).ToListAsync(ct);
        if (users.Count != 1 || users[0].Nombre != "admin" || users[0].Rol != UserRoles.Jefa || !users[0].Activo)
            throw new InvalidOperationException("Estado de usuarios inesperado.");
        var now = DateTimeOffset.UtcNow;
        if (await db.SesionesUsuario.AnyAsync(x => x.FechaRevocacion == null && x.ExpiracionAbsoluta > now, ct))
            throw new InvalidOperationException("Hay sesiones no revocadas. Cierre las sesiones antes de continuar.");
        var admin = users[0];
        admin.Nombre = "jefa";
        admin.ContrasenaHash = hasher.Hash(jefa);
        admin.IntentosFallidos = 0;
        admin.BloqueoHasta = null;
        db.Usuarios.AddRange(
            new Usuario { Nombre = "cocina", Email = "cocina@elcarbonazo.local", Rol = UserRoles.PersonalCocina, ContrasenaHash = hasher.Hash(cocina), Activo = true },
            new Usuario { Nombre = "mesero", Email = "mesero@elcarbonazo.local", Rol = UserRoles.Mesero, ContrasenaHash = hasher.Hash(mesero), Activo = true });
        await db.SaveChangesAsync(ct);
        audit.Record("CuentasPruebaDefensaPreparadas", "Exitoso", admin.IdUsuario,
            entityType: "usuario", entityId: admin.IdUsuario.ToString());
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
