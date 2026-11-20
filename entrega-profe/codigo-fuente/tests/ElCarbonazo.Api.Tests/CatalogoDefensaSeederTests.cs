using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class CatalogoDefensaSeederTests
{
    [Fact]
    public async Task Fallo_de_auditoria_revierte_carga_completa()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            var name = new NpgsqlConnectionStringBuilder(database.ConnectionString).Database!;
            await using (var context = database.CreateContext())
            {
                var actor = await context.Usuarios.Where(x => x.Rol == "Jefa" && x.Activo).Select(x => x.IdUsuario).SingleAsync();
                var seeder = new CatalogoDefensaSeeder(context, new FailingAudit(), TimeProvider.System);
                await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.CargarAsync(FindData(), actor, name, default));
            }
            await using var verify = database.CreateContext();
            Assert.Equal(0, await verify.Productos.CountAsync());
            Assert.Equal(0, await verify.Ingredientes.CountAsync());
            Assert.Equal(0, await verify.Recetas.CountAsync());
            Assert.Equal(0, await verify.MovimientosInventario.CountAsync());
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Carga_completa_es_atomica_y_no_sobrescribe()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();
            var actor = await context.Usuarios.Where(x => x.Rol == "Jefa" && x.Activo).Select(x => x.IdUsuario).SingleAsync();
            var mesa = await context.Mesas.FirstAsync();
            context.Pedidos.Add(new Pedido
            {
                IdMesa = mesa.IdMesa,
                IdUsuario = actor,
                FechaHora = DateTimeOffset.UtcNow,
                Estado = "Abierto",
                Subtotal = 0,
                Impuestos = 0,
                Total = 0
            });
            await context.SaveChangesAsync();
            var existingOrders = await context.Pedidos.CountAsync();
            var directory = FindData();
            var name = new NpgsqlConnectionStringBuilder(database.ConnectionString).Database!;
            var seeder = new CatalogoDefensaSeeder(context,
                new AuditRecorder(context, new HttpContextAccessor(), TimeProvider.System), TimeProvider.System);
            await seeder.CargarAsync(directory, actor, name, default);
            Assert.Equal(90, await context.Productos.CountAsync());
            Assert.Equal(116, await context.Ingredientes.CountAsync());
            Assert.Equal(90, await context.Recetas.CountAsync());
            Assert.Equal(452, await context.DetallesReceta.CountAsync());
            Assert.Equal(116, await context.MovimientosInventario.CountAsync());
            Assert.Equal(existingOrders, await context.Pedidos.CountAsync());
            context.ChangeTracker.Clear();
            await seeder.CargarAsync(directory, actor, name, default);
            Assert.Equal(116, await context.MovimientosInventario.CountAsync());
            var product = await context.Productos.FirstAsync();
            product.Precio += 1;
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.CargarAsync(directory, actor, name, default));
            Assert.Equal(existingOrders, await context.Pedidos.CountAsync());
        }
        finally { await database.DisposeAsync(); }
    }

    private sealed class FailingAudit : IAuditRecorder
    {
        public void Record(string eventType, string result, int? userId = null, Guid? sessionId = null,
            string? entityType = null, string? entityId = null, IReadOnlyDictionary<string, object?>? details = null)
            => throw new InvalidOperationException("Fallo simulado de auditoría.");
    }

    private static string FindData()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ElCarbonazo.sln")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Falta raíz."),
            "specs", "entrega-defensa-local");
    }
}
