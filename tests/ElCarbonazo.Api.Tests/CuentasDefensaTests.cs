using ElCarbonazo.Api.Configuration;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class CuentasDefensaTests
{
    [Fact]
    public async Task Preparacion_conserva_identidad_y_rechaza_repeticion()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var context = db.CreateContext();
            await context.Database.ExecuteSqlRawAsync("DELETE FROM usuario WHERE id_usuario = 2; UPDATE usuario SET nombre = 'admin' WHERE id_usuario = 1");
            var hash = new BCryptPasswordHasher(Options.Create(new PasswordHashingOptions { WorkFactor = 10 }));
            var audit = new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System);
            await CuentasDefensaCommand.PrepararAsync(context, hash, audit, "test-jefa", "test-cocina", "test-mesero", default);
            context.ChangeTracker.Clear();
            var users = await context.Usuarios.OrderBy(x => x.IdUsuario).ToListAsync();
            Assert.Equal(3, users.Count);
            Assert.Equal((1, "jefa", "Jefa", "jefa@example.local"), (users[0].IdUsuario, users[0].Nombre, users[0].Rol, users[0].Email));
            Assert.Equal("Personal de Cocina", users.Single(x => x.Nombre == "cocina").Rol);
            Assert.Equal("Mesero", users.Single(x => x.Nombre == "mesero").Rol);
            Assert.True(hash.Verify("test-jefa", users[0].ContrasenaHash));
            Assert.True(hash.Verify("test-cocina", users.Single(x => x.Nombre == "cocina").ContrasenaHash));
            Assert.True(hash.Verify("test-mesero", users.Single(x => x.Nombre == "mesero").ContrasenaHash));
            Assert.Single(await context.EventosAuditoria.Where(x => x.TipoEvento == "CuentasPruebaDefensaPreparadas").ToListAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CuentasDefensaCommand.PrepararAsync(context, hash, audit, "x", "y", "z", default));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Rechaza_usuarios_inesperados_sin_cambiar_cuentas()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var context = db.CreateContext();
            var hash = new BCryptPasswordHasher(Options.Create(new PasswordHashingOptions { WorkFactor = 10 }));
            var audit = new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CuentasDefensaCommand.PrepararAsync(context, hash, audit, "x", "y", "z", default));
            context.ChangeTracker.Clear();
            Assert.Equal(2, await context.Usuarios.CountAsync());
            Assert.False(await context.EventosAuditoria.AnyAsync(x => x.TipoEvento == "CuentasPruebaDefensaPreparadas"));
        }
        finally { await db.DisposeAsync(); }
    }
}
