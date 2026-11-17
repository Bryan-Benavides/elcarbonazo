using ElCarbonazo.Api.Configuration;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class AnulacionAutorizacionTests
{
    [Fact]
    public async Task Authorization_binds_operation_to_intention_and_stores_only_token_hash()
    {
        var db = new TemporaryPostgresDatabase();
        try
        {
            await db.InitializeAsync();
            await using var context = db.CreateContext();
            var key = Enumerable.Repeat((byte)37, 32).ToArray();
            var audit = new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System);
            Assert.Equal(ResultadoProvisionHmac.Provisionado,
                await new HmacAutorizacionProvisioner(context, audit).ProvisionarInicialAsync(key, default));
            var session = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO sesion_usuario (id_sesion, id_usuario, fecha_creacion, ultima_actividad, expiracion_absoluta)
                VALUES ({session}, 1, now(), now(), now() + interval '1 hour')
                """);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AutorizacionSuperior:ClaveHmac"] = Convert.ToBase64String(key)
            }).Build();
            var hasher = new BCryptPasswordHasher(Options.Create(new PasswordHashingOptions { WorkFactor = 10 }));
            var service = new AnulacionAutorizacionService(context, hasher, audit, TimeProvider.System, config);
            var request = new AutorizarAnulacionRequest(Guid.NewGuid(), 1, 1, null, "Motivo valido", "jefa@example.local", "correct horse battery staple");
            var result = await service.AutorizarAsync(request, 1, session, default);
            Assert.Equal(200, result.Status);
            Assert.NotNull(result.Data?.Token);
            Assert.InRange(result.Data!.ExpiraUtc - DateTimeOffset.UtcNow, TimeSpan.FromSeconds(118), TimeSpan.FromSeconds(120));
            Assert.Equal("Autorizada", (await context.OperacionesAnulacion.SingleAsync()).Estado);
            var tokenRow = await context.AutorizacionesAnulacion.SingleAsync();
            Assert.NotEqual(result.Data.Token, tokenRow.HashToken);
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(result.Data.Token))), tokenRow.HashToken);
            var mismatched = await service.AutorizarAsync(request with { Motivo = "Otro motivo valido" }, 1, session, default);
            Assert.Equal("OPERACION_CLAVE_EN_USO", mismatched.Code);
            Assert.Empty(await context.LimitesAutorizacion.ToListAsync());
            var bad = request with { IdOperacion = Guid.NewGuid(), Contrasena = "contraseña incorrecta" };
            for (var attempt = 0; attempt < 5; attempt++)
                Assert.Equal(403, (await service.AutorizarAsync(bad, 1, session, default)).Status);
            Assert.Equal(429, (await service.AutorizarAsync(bad, 1, session, default)).Status);
            Assert.Equal(5, (await context.LimitesAutorizacion.SingleAsync()).IntentosFallidos);
            Assert.DoesNotContain("contraseña incorrecta", await context.EventosAuditoria.Select(x => x.Detalles).ToListAsync());
        }
        finally { await db.DisposeAsync(); }
    }
}
