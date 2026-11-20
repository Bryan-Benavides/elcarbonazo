using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class CajaCancelacionTests
{
    [Fact]
    public async Task Other_cashier_cannot_enumerate_and_supervisor_needs_confirmation()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            var id = Guid.NewGuid();
            int cashierId;
            await using (var setup = database.CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
                await setup.Database.ExecuteSqlRawAsync("SELECT setval(pg_get_serial_sequence('usuario', 'id_usuario'), (SELECT max(id_usuario) FROM usuario))");
                var cashier = new Usuario { Nombre = "Caja prueba", Email = "caja-prueba@example.invalid", ContrasenaHash = "test", Rol = UserRoles.EncargadoCaja, Activo = true };
                setup.Usuarios.Add(cashier);
                await setup.SaveChangesAsync();
                cashierId = cashier.IdUsuario;
                setup.OperacionesCaja.Add(new OperacionCaja
                {
                    IdOperacion = id,
                    IdUsuarioPropietario = 1,
                    Tipo = "AbrirTurno",
                    VersionContrato = 1,
                    Argumentos = "{}",
                    HashArgumentos = new string('a', 64),
                    Estado = CajaStates.OperacionPreparada,
                    FechaCreacion = DateTimeOffset.UtcNow
                });
                await setup.SaveChangesAsync();
            }
            await using (var context = database.CreateContext())
            {
                var service = new CajaCancelacionService(context, new AuditRecorder(context, new HttpContextAccessor(), TimeProvider.System), TimeProvider.System);
                var denied = await service.CancelarAsync(id, cashierId, true, CancellationToken.None);
                Assert.Equal(404, denied.Status);
                Assert.Null(denied.Estado);
            }
            // Reassign ownership only in this isolated fixture, then verify supervisory confirmation.
            await using (var setup = database.CreateContext())
                await setup.Database.ExecuteSqlInterpolatedAsync($"UPDATE operacion_caja SET id_usuario_propietario = {cashierId} WHERE id_operacion = {id}");
            await using (var context = database.CreateContext())
            {
                var service = new CajaCancelacionService(context, new AuditRecorder(context, new HttpContextAccessor(), TimeProvider.System), TimeProvider.System);
                Assert.Equal(400, (await service.CancelarAsync(id, 1, false, CancellationToken.None)).Status);
                Assert.Equal(200, (await service.CancelarAsync(id, 1, true, CancellationToken.None)).Status);
                Assert.Equal(200, (await service.CancelarAsync(id, 1, true, CancellationToken.None)).Status);
            }
            await using var verify = database.CreateContext();
            Assert.Equal(2, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "IntervencionOperacionCaja"));
            Assert.Equal(1, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "OperacionCajaCancelada"));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task Concurrent_cancellation_creates_one_durable_barrier_and_one_audit_event()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using (var setup = database.CreateContext())
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
            var id = Guid.NewGuid();
            async Task<CajaCancelacionResult> Cancel()
            {
                await using var context = database.CreateContext();
                var audit = new AuditRecorder(context, new HttpContextAccessor(), TimeProvider.System);
                return await new CajaCancelacionService(context, audit, TimeProvider.System).CancelarAsync(id, 1, false, CancellationToken.None);
            }
            var results = await Task.WhenAll(Cancel(), Cancel());
            Assert.All(results, result => Assert.Equal(CajaStates.OperacionCancelada, result.Estado));
            await using var verify = database.CreateContext();
            var barrier = await verify.OperacionesCaja.SingleAsync(x => x.IdOperacion == id);
            Assert.Null(barrier.Tipo);
            Assert.Null(barrier.Argumentos);
            Assert.Equal(1, barrier.IdUsuarioPropietario);
            Assert.Equal(1, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "OperacionCajaCancelada"));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task Audit_failure_rolls_back_barrier()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();
            await context.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
            var id = Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new CajaCancelacionService(context, new FailingAudit(), TimeProvider.System).CancelarAsync(id, 1, false, CancellationToken.None));
            await using var verify = database.CreateContext();
            Assert.False(await verify.OperacionesCaja.AnyAsync(x => x.IdOperacion == id));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    private sealed class FailingAudit : IAuditRecorder
    {
        public void Record(string eventType, string result, int? userId = null, Guid? sessionId = null,
            string? entityType = null, string? entityId = null, IReadOnlyDictionary<string, object?>? details = null) =>
            throw new InvalidOperationException("Injected audit failure");
    }
}
