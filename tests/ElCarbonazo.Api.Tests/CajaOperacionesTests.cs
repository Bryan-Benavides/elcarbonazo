using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class CajaOperacionesTests
{
    [Theory]
    [InlineData("success", CajaStates.OperacionCompletada, 1)]
    [InlineData("reject", CajaStates.OperacionRechazada, 0)]
    [InlineData("exception", CajaStates.OperacionPreparada, 0)]
    public async Task Execute_commits_or_rolls_back_domain_and_terminal_together(string behavior, string state, int auditCount)
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            var id = Guid.NewGuid();
            await using (var setup = db.CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
                await CreateService(setup).PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(10, true), default);
            }
            await using (var context = db.CreateContext())
            {
                var executor = new FakeExecutor(context, behavior);
                var service = new CajaOperacionesService(context, Audit(context), TimeProvider.System, executor);
                if (behavior == "exception")
                    await Assert.ThrowsAsync<InvalidOperationException>(() => service.EjecutarAsync(id, 1, default));
                else
                {
                    Assert.Equal(state, (await service.EjecutarAsync(id, 1, default)).Estado);
                    Assert.Equal(state, (await service.EjecutarAsync(id, 1, default)).Estado);
                    Assert.Equal(1, executor.Calls);
                }
            }
            await using var verify = db.CreateContext();
            Assert.Equal(state, await verify.OperacionesCaja.Where(x => x.IdOperacion == id).Select(x => x.Estado).SingleAsync());
            Assert.Equal(auditCount, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "PruebaCaja"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Cancel_started_during_execution_replays_committed_result()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            var id = Guid.NewGuid();
            await using (var setup = db.CreateContext())
            {
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
                await CreateService(setup).PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(10, true), default);
            }
            var executor = new BlockingExecutor();
            await using var executionContext = db.CreateContext();
            var execution = new CajaOperacionesService(executionContext, Audit(executionContext), TimeProvider.System, executor)
                .EjecutarAsync(id, 1, default);
            try
            {
                await executor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                await using var cancellationContext = db.CreateContext();
                var cancellation = new CajaCancelacionService(cancellationContext, Audit(cancellationContext), TimeProvider.System)
                    .CancelarAsync(id, 1, false, default);
                executor.Release.TrySetResult();
                Assert.Equal(CajaStates.OperacionCompletada, (await execution.WaitAsync(TimeSpan.FromSeconds(20))).Estado);
                Assert.Equal(CajaStates.OperacionCompletada, (await cancellation.WaitAsync(TimeSpan.FromSeconds(20))).Estado);
                await using var verify = db.CreateContext();
                Assert.Empty(await verify.EventosAuditoria.Where(x => x.TipoEvento == "OperacionCajaCancelada").ToListAsync());
            }
            finally { executor.Release.TrySetResult(); }
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Payment_preparation_rejects_missing_shift_without_persisting_intent()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var context = db.CreateContext();
            await context.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
            var intent = CajaOperacionCanonica.Cobrar(1, 999, 10, new string('a', 64), CajaStates.Efectivo, 10, null, false, false);
            Assert.Equal("TURNO_NO_ABIERTO", (await CreateService(context).PrepararAsync(Guid.NewGuid(), 1, intent, default)).Code);
            Assert.Empty(await context.OperacionesCaja.ToListAsync());
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Prepare_replays_identical_payload_rejects_changes_and_execute_fails_closed()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var context = db.CreateContext();
            await context.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
            var service = CreateService(context);
            var id = Guid.NewGuid();
            var request = CajaOperacionCanonica.Abrir(10m, true);
            Assert.Equal(CajaStates.OperacionPreparada, (await service.PrepararAsync(id, 1, request, default)).Estado);
            Assert.Equal(CajaStates.OperacionPreparada, (await service.PrepararAsync(id, 1, request, default)).Estado);
            Assert.Equal(409, (await service.PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(11m, true), default)).Status);
            Assert.Equal(CajaStates.OperacionPreparada, (await service.ConsultarAsync(id, 1, default)).Estado);
            Assert.Equal(503, (await service.EjecutarAsync(id, 1, default)).Status);
            Assert.Equal(CajaStates.OperacionPreparada, (await context.OperacionesCaja.AsNoTracking().SingleAsync()).Estado);
            Assert.Empty(await context.TurnosCaja.ToListAsync());
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Concurrent_prepare_and_cancel_never_remove_barrier_and_terminal_replays()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using (var setup = db.CreateContext())
                await setup.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Jefa', activo = true WHERE id_usuario = 1");
            var id = Guid.NewGuid();
            async Task<CajaCancelacionResult> Prepare()
            {
                await using var context = db.CreateContext();
                return await CreateService(context).PrepararAsync(id, 1, CajaOperacionCanonica.Abrir(10, true), default);
            }
            async Task<CajaCancelacionResult> Cancel()
            {
                await using var context = db.CreateContext();
                return await new CajaCancelacionService(context, Audit(context), TimeProvider.System).CancelarAsync(id, 1, false, default);
            }
            var results = await Task.WhenAll(Prepare(), Cancel());
            Assert.Contains(results, x => x.Estado == CajaStates.OperacionCancelada);
            await using var verify = db.CreateContext();
            var operation = await verify.OperacionesCaja.AsNoTracking().SingleAsync(x => x.IdOperacion == id);
            Assert.Equal(CajaStates.OperacionCancelada, operation.Estado);
            Assert.Equal(1, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "OperacionCajaCancelada"));
            var replay = await CreateService(verify).EjecutarAsync(id, 1, default);
            Assert.Equal(CajaStates.OperacionCancelada, replay.Estado);
            Assert.Empty(await verify.TurnosCaja.ToListAsync());
        }
        finally { await db.DisposeAsync(); }
    }

    private static CajaOperacionesService CreateService(ElCarbonazo.Api.Data.AppDbContext context) =>
        new(context, Audit(context), TimeProvider.System);
    private static AuditRecorder Audit(ElCarbonazo.Api.Data.AppDbContext context) =>
        new(context, new HttpContextAccessor(), TimeProvider.System);

    private sealed class FakeExecutor(ElCarbonazo.Api.Data.AppDbContext context, string behavior) : ICajaEjecutorDominio
    {
        public int Calls { get; private set; }
        public async Task<CajaResultadoDominio> EjecutarAsync(CajaEjecucionPreparada operacion, CancellationToken cancellationToken)
        {
            Calls++;
            Audit(context).Record("PruebaCaja", "Exitoso", operacion.IdUsuario);
            await context.SaveChangesAsync(cancellationToken);
            if (behavior == "exception") throw new InvalidOperationException("Injected domain failure");
            return behavior == "success"
                ? new(true, 200, "OK", "{\"ok\":true}")
                : new(false, 409, "CONFLICTO", "{\"ok\":false}");
        }
    }

    private sealed class BlockingExecutor : ICajaEjecutorDominio
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CajaResultadoDominio> EjecutarAsync(CajaEjecucionPreparada operacion, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(true, 200, "OK", "{}");
        }
    }
}
