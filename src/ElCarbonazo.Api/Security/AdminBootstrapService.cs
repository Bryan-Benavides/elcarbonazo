using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace ElCarbonazo.Api.Security;

public sealed class AdminBootstrapService(
    AppDbContext context,
    IPasswordHasher passwordHasher,
    IHostEnvironment environment,
    IAuditRecorder auditRecorder) : IAdminBootstrapService
{
    public async Task BootstrapAsync(
        string nombre,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        if (!BootstrapPasswordPolicy.IsAcceptable(password))
        {
            throw new InvalidOperationException(
                "La contraseña debe tener al menos 10 caracteres, máximo 64 bytes UTF-8 y no estar en la lista local de contraseñas comunes.");
        }

        await CreateInitialAdminAsync(
            nombre,
            email,
            password,
            rejectExistingUsers: true,
            auditEvent: "BootstrapAdmin",
            cancellationToken);
    }

    public Task SeedDevelopmentAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("El seeder admin/admin123 solo puede ejecutarse en Development.");
        }

        return CreateInitialAdminAsync(
            "admin",
            "admin@elcarbonazo.local",
            "admin123",
            rejectExistingUsers: false,
            auditEvent: "BootstrapAdminDevelopment",
            cancellationToken);
    }

    private async Task CreateInitialAdminAsync(
        string nombre,
        string email,
        string password,
        bool rejectExistingUsers,
        string auditEvent,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        await context.Database.ExecuteSqlRawAsync(
            "LOCK TABLE usuario IN EXCLUSIVE MODE",
            cancellationToken);

        var users = await context.Usuarios
            .OrderBy(user => user.IdUsuario)
            .ToListAsync(cancellationToken);

        Usuario target;
        if (users.Count == 0)
        {
            target = new Usuario();
            context.Usuarios.Add(target);
        }
        else if (users.Count == 1 && IsNeutralizedLegacyAdmin(users[0]))
        {
            target = users[0];
        }
        else
        {
            if (rejectExistingUsers)
            {
                throw new InvalidOperationException(
                    "Bootstrap rechazado: usuario ya contiene cuentas distintas de la fila heredada neutralizada.");
            }

            return;
        }

        target.Nombre = nombre;
        target.Email = email;
        target.Rol = UserRoles.Jefa;
        target.Activo = true;
        target.IntentosFallidos = 0;
        target.BloqueoHasta = null;
        target.ContrasenaHash = passwordHasher.Hash(password);

        await context.SaveChangesAsync(cancellationToken);
        auditRecorder.Record(
            auditEvent,
            "Exitoso",
            target.IdUsuario,
            entityType: "usuario",
            entityId: target.IdUsuario.ToString());
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool IsNeutralizedLegacyAdmin(Usuario user) =>
        user.IdUsuario == 1 &&
        user.Nombre == LegacyAdminSeed.Name &&
        user.Email == LegacyAdminSeed.Email &&
        user.ContrasenaHash == LegacyAdminSeed.NeutralizedHash &&
        user.Rol == UserRoles.Jefa &&
        !user.Activo;
}

public static class LegacyAdminSeed
{
    public const string Name = "admin";
    public const string Email = "admin@elcarbonazo.local";
    public const string OriginalHash = "$2a$12$ldnQEKzFtA3tYTUTCuXBTOWQPHOHLphZT6zIMMFAusyK0dGgdH9GG";
    public const string NeutralizedHash = "!NEUTRALIZED-LEGACY-ADMIN!";
}
