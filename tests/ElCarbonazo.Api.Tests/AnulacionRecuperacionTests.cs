using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class AnulacionRecuperacionTests
{
    [Fact]
    public async Task Terminal_result_is_preserved_and_failed_audit_rolls_back_new_barrier()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var context = db.CreateContext();
            var session = Guid.NewGuid();
            var completed = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO sesion_usuario (id_sesion, id_usuario, fecha_creacion, ultima_actividad, expiracion_absoluta)
                VALUES ({session}, 1, now(), now(), now() + interval '1 hour');
                INSERT INTO operacion_anulacion (id_operacion, id_usuario_propietario, hash_intencion, estado,
                    fecha_creacion_utc, fecha_terminal_utc, status_resultado, codigo_resultado, resultado_json)
                VALUES ({completed}, 1, repeat('a', 64), 'Completada', now(), now(), 201, 'OK', jsonb_build_object())
                """);
            var normal = new AnulacionRecuperacionService(context, new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
            var result = await normal.ResolverAsync(completed, 1, session, true, false, default);
            Assert.Equal(201, result.Status);
            Assert.Equal("Completada", result.Estado);
            var id = Guid.NewGuid();
            var failing = new AnulacionRecuperacionService(context, new FailingAudit(), TimeProvider.System);
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.ResolverAsync(id, 1, session, true, false, default));
            context.ChangeTracker.Clear();
            Assert.False(await context.OperacionesAnulacion.AnyAsync(x => x.IdOperacion == id));
        }
        finally { await db.DisposeAsync(); }
    }

    private sealed class FailingAudit : IAuditRecorder
    {
        public void Record(string eventType, string result, int? userId = null, Guid? sessionId = null,
            string? entityType = null, string? entityId = null, IReadOnlyDictionary<string, object?>? details = null) =>
            throw new InvalidOperationException("Audit failure");
    }

    [Fact]
    public async Task Barrier_is_durable_idempotent_and_rejects_revoked_session()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var context = db.CreateContext();
            var session = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO sesion_usuario (id_sesion, id_usuario, fecha_creacion, ultima_actividad, expiracion_absoluta)
                VALUES ({session}, 1, now(), now(), now() + interval '1 hour')
                """);
            var service = new AnulacionRecuperacionService(context, new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
            var id = Guid.NewGuid();
            Assert.Equal(404, (await service.ResolverAsync(id, 1, session, false, false, default)).Status);
            Assert.Equal("Cancelada", (await service.ResolverAsync(id, 1, session, true, false, default)).Estado);
            context.ChangeTracker.Clear();
            Assert.Equal("Cancelada", (await service.ResolverAsync(id, 1, session, true, false, default)).Estado);
            Assert.Equal(1, await context.EventosAuditoria.CountAsync(x => x.TipoEvento == "BarreraOperacionAnulacion"));
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE sesion_usuario SET fecha_revocacion = now() WHERE id_sesion = {session}");
            Assert.Equal(401, (await service.ResolverAsync(id, 1, session, false, false, default)).Status);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Concurrent_barriers_create_one_terminal_record_and_one_event()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var setup = db.CreateContext();
            var session = Guid.NewGuid();
            await setup.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO sesion_usuario (id_sesion, id_usuario, fecha_creacion, ultima_actividad, expiracion_absoluta)
                VALUES ({session}, 1, now(), now(), now() + interval '1 hour')
                """);
            await using var first = db.CreateContext();
            await using var second = db.CreateContext();
            var a = new AnulacionRecuperacionService(first, new AuditRecorder(first, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
            var b = new AnulacionRecuperacionService(second, new AuditRecorder(second, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
            var id = Guid.NewGuid();
            var results = await Task.WhenAll(a.ResolverAsync(id, 1, session, true, false, default), b.ResolverAsync(id, 1, session, true, false, default));
            Assert.All(results, result => Assert.Equal("Cancelada", result.Estado));
            Assert.Equal(1, await setup.OperacionesAnulacion.CountAsync(x => x.IdOperacion == id));
            Assert.Equal(1, await setup.EventosAuditoria.CountAsync(x => x.TipoEvento == "BarreraOperacionAnulacion"));
        }
        finally { await db.DisposeAsync(); }
    }
}
