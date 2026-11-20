using ElCarbonazo.Api.Security;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class HmacProvisionPersistenceTests
{
    [Fact]
    public async Task Initial_provision_is_explicit_audited_and_never_repairs_missing_state()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();
            var audit = new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System);
            var service = new HmacAutorizacionProvisioner(context, audit);
            var key = Enumerable.Repeat((byte)7, 32).ToArray();
            Assert.Equal(ResultadoProvisionHmac.ClaveInvalida, await service.ProvisionarInicialAsync(new byte[31], default));
            Assert.Equal(ResultadoProvisionHmac.Provisionado, await service.ProvisionarInicialAsync(key, default));
            Assert.Equal(ResultadoProvisionHmac.YaProvisionado, await service.ProvisionarInicialAsync(key, default));
            var events = await context.EventosAuditoria.AsNoTracking()
                .Where(x => x.TipoEvento == "HmacAutorizacionProvisionado").ToArrayAsync();
            Assert.Single(events);
            Assert.DoesNotContain(HmacAutorizacionGuard.CalcularHuella(key), events[0].Detalles, StringComparison.Ordinal);
            await context.Database.ExecuteSqlRawAsync("DELETE FROM estado_hmac_autorizacion WHERE id = 1");
            Assert.Equal(ResultadoProvisionHmac.MetadatosAusentes, await service.ProvisionarInicialAsync(key, default));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Provision_rejects_existing_evidence_and_rolls_back_when_audit_fails()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();
            var key = Enumerable.Repeat((byte)8, 32).ToArray();
            var failing = new HmacAutorizacionProvisioner(context, new FailingAudit());
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.ProvisionarInicialAsync(key, default));
            var notProvisioned = await context.Database.SqlQueryRaw<bool>(
                "SELECT NOT provisionado AS \"Value\" FROM estado_hmac_autorizacion WHERE id = 1").SingleAsync();
            Assert.True(notProvisioned);
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO limite_autorizacion (id_usuario_solicita, hash_nombre, intentos_fallidos, ultima_actividad_utc)
                VALUES (1, repeat('a', 64), 1, now())
                """);
            var service = new HmacAutorizacionProvisioner(context, new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System));
            Assert.Equal(ResultadoProvisionHmac.EvidenciaExistente, await service.ProvisionarInicialAsync(key, default));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Concurrent_provision_has_one_winner()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var first = database.CreateContext();
            await using var second = database.CreateContext();
            var key = Enumerable.Repeat((byte)9, 32).ToArray();
            var firstService = new HmacAutorizacionProvisioner(first, new AuditRecorder(first, new TestHttpContextAccessor(), TimeProvider.System));
            var secondService = new HmacAutorizacionProvisioner(second, new AuditRecorder(second, new TestHttpContextAccessor(), TimeProvider.System));
            var results = await Task.WhenAll(firstService.ProvisionarInicialAsync(key, default), secondService.ProvisionarInicialAsync(key, default));
            Assert.Single(results, x => x == ResultadoProvisionHmac.Provisionado);
            Assert.Single(results, x => x == ResultadoProvisionHmac.YaProvisionado);
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Recovery_of_missing_metadata_is_audited_and_blocks_for_24_hours_without_erasing_evidence()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();
            var clock = new RecoveryClock { Now = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero) };
            var key = Enumerable.Repeat((byte)13, 32).ToArray();
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO limite_autorizacion (id_usuario_solicita, hash_nombre, intentos_fallidos, ultima_actividad_utc)
                VALUES (1, repeat('a', 64), 3, now());
                DELETE FROM estado_hmac_autorizacion WHERE id = 1
                """);
            var failing = new HmacAutorizacionRecovery(context, new FailingAudit(), clock);
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.RecuperarAusenteAsync(key, default));
            Assert.Equal(0, await context.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM estado_hmac_autorizacion").SingleAsync());
            var recovery = new HmacAutorizacionRecovery(context,
                new AuditRecorder(context, new TestHttpContextAccessor(), clock), clock);
            Assert.Equal(ResultadoRecoveryHmac.RecuperadoEnEspera, await recovery.RecuperarAusenteAsync(key, default));
            Assert.Equal(ResultadoRecoveryHmac.EstadoExistente, await recovery.RecuperarAusenteAsync(key, default));
            Assert.Equal(3, (await context.LimitesAutorizacion.SingleAsync()).IntentosFallidos);
            Assert.Equal(1, await context.EventosAuditoria.CountAsync(x => x.TipoEvento == "HmacAutorizacionRecuperado"));
            var guard = new HmacAutorizacionGuard(context, clock);
            var calls = 0;
            Assert.Equal(EstadoHmacAutorizacion.Espera,
                await guard.EjecutarAsync(key, _ => { calls++; return Task.CompletedTask; }, default));
            clock.Now = clock.Now.AddHours(24).AddTicks(-1);
            Assert.Equal(EstadoHmacAutorizacion.Espera,
                await guard.EjecutarAsync(key, _ => { calls++; return Task.CompletedTask; }, default));
            clock.Now = clock.Now.AddTicks(1);
            Assert.Equal(EstadoHmacAutorizacion.Disponible,
                await guard.EjecutarAsync(key, _ => { calls++; return Task.CompletedTask; }, default));
            Assert.Equal(1, calls);
        }
        finally { await database.DisposeAsync(); }
    }

    private sealed class RecoveryClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Concurrent_recovery_has_one_winner_and_one_audit_event()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var first = database.CreateContext();
            await using var second = database.CreateContext();
            await first.Database.ExecuteSqlRawAsync("DELETE FROM estado_hmac_autorizacion WHERE id = 1");
            var clock = new RecoveryClock { Now = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero) };
            var key = Enumerable.Repeat((byte)14, 32).ToArray();
            var a = new HmacAutorizacionRecovery(first, new AuditRecorder(first, new TestHttpContextAccessor(), clock), clock);
            var b = new HmacAutorizacionRecovery(second, new AuditRecorder(second, new TestHttpContextAccessor(), clock), clock);
            var results = await Task.WhenAll(a.RecuperarAusenteAsync(key, default), b.RecuperarAusenteAsync(key, default));
            Assert.Single(results, x => x == ResultadoRecoveryHmac.RecuperadoEnEspera);
            Assert.Single(results, x => x == ResultadoRecoveryHmac.EstadoExistente);
            Assert.Equal(1, await first.EventosAuditoria.CountAsync(x => x.TipoEvento == "HmacAutorizacionRecuperado"));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Provision_command_requires_exact_database_confirmation_and_distinct_secret()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();
            await context.Database.OpenConnectionAsync();
            await using var query = context.Database.GetDbConnection().CreateCommand();
            query.CommandText = "SELECT current_database(), COALESCE(inet_server_addr()::text, 'local'), inet_server_port()";
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var destination = $"{reader.GetString(0)}@{reader.GetString(1)}:{reader.GetInt32(2)}";
            await reader.CloseAsync();
            var key = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());
            var jwt = Convert.ToBase64String(Enumerable.Repeat((byte)8, 32).ToArray());
            var audit = new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System);
            var output = new StringWriter();
            Assert.Equal(1, await HmacProvisionCommand.RunAsync(context, audit, key, key,
                new StringReader($"APROVISIONAR {destination}"), output, default));
            Assert.Equal(1, await HmacProvisionCommand.RunAsync(context, audit, key, jwt,
                new StringReader("destino incorrecto"), output, default));
            Assert.True(await context.Database.SqlQueryRaw<bool>(
                "SELECT NOT provisionado AS \"Value\" FROM estado_hmac_autorizacion WHERE id = 1").SingleAsync());
            Assert.Equal(0, await HmacProvisionCommand.RunAsync(context, audit, key, jwt,
                new StringReader($"APROVISIONAR {destination}"), output, default));
            Assert.Equal(1, await HmacProvisionCommand.RunAsync(context, audit, key, jwt,
                new StringReader($"APROVISIONAR {destination}"), output, default));
            await context.Database.ExecuteSqlRawAsync("DELETE FROM estado_hmac_autorizacion WHERE id = 1");
            Assert.Equal(1, await HmacProvisionCommand.RunAsync(context, audit, key, jwt,
                new StringReader($"APROVISIONAR {destination}"), output, default, recuperar: true));
            Assert.Equal(0, await context.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM estado_hmac_autorizacion").SingleAsync());
            Assert.Equal(0, await HmacProvisionCommand.RunAsync(context, audit, key, jwt,
                new StringReader($"RECUPERAR {destination}"), output, default, recuperar: true));
            Assert.True(await context.Database.SqlQueryRaw<bool>(
                "SELECT espera_hasta_utc > now() AS \"Value\" FROM estado_hmac_autorizacion WHERE id = 1").SingleAsync());
            Assert.DoesNotContain(key, output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(jwt, output.ToString(), StringComparison.Ordinal);
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Provision_command_runs_as_a_separate_process_against_temporary_database()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();
            await context.Database.OpenConnectionAsync();
            await using var query = context.Database.GetDbConnection().CreateCommand();
            query.CommandText = "SELECT current_database(), COALESCE(inet_server_addr()::text, 'local'), inet_server_port()";
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var destination = $"{reader.GetString(0)}@{reader.GetString(1)}:{reader.GetInt32(2)}";
            await reader.CloseAsync();
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ElCarbonazo.sln")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var apiDirectory = Path.Combine(directory.FullName, "src", "ElCarbonazo.Api");
            var apiAssembly = Path.Combine(apiDirectory, "bin", "Debug", "net10.0", "ElCarbonazo.Api.dll");
            Assert.True(File.Exists(apiAssembly), "Se requiere build Debug de la API.");
            var key = Convert.ToBase64String(Enumerable.Repeat((byte)11, 32).ToArray());
            var jwt = Convert.ToBase64String(Enumerable.Repeat((byte)12, 32).ToArray());
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = apiDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add(apiAssembly);
            start.ArgumentList.Add("--provisionar-hmac-autorizacion");
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["ConnectionStrings__DefaultConnection"] = database.ConnectionString;
            start.Environment["AutorizacionSuperior__ClaveHmac"] = key;
            start.Environment["Jwt__Key"] = jwt;
            start.Environment["Logging__FileDirectory"] = database.LogDirectory;
            using var process = Process.Start(start);
            Assert.NotNull(process);
            await process.StandardInput.WriteLineAsync($"APROVISIONAR {destination}");
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            var output = await stdout + await stderr;
            Assert.Equal(0, process.ExitCode);
            Assert.Contains("Primera provisión completada.", output, StringComparison.Ordinal);
            Assert.DoesNotContain(key, output, StringComparison.Ordinal);
            Assert.DoesNotContain(jwt, output, StringComparison.Ordinal);
            Assert.True(await context.Database.SqlQueryRaw<bool>(
                "SELECT provisionado AS \"Value\" FROM estado_hmac_autorizacion WHERE id = 1").SingleAsync());
        }
        finally { await database.DisposeAsync(); }
    }

    private sealed class FailingAudit : IAuditRecorder
    {
        public void Record(string eventType, string result, int? userId = null, Guid? sessionId = null,
            string? entityType = null, string? entityId = null, IReadOnlyDictionary<string, object?>? details = null) =>
            throw new InvalidOperationException("Fallo de auditoría ficticio.");
    }
}
