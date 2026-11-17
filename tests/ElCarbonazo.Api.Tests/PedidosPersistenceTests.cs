using System.Text.Json;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Services;
using ElCarbonazo.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class PedidosPersistenceTests : IAsyncLifetime
{
    private readonly TemporaryPostgresDatabase _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Shared_unicode_cases_match_api_validation_and_persistence()
    {
        int productId;
        await using (var setup = _database.CreateContext())
        {
            var product = new Producto { Nombre = "Prueba", Categoria = "Comida", Precio = 1m, Disponible = true };
            setup.Productos.Add(product);
            await setup.SaveChangesAsync();
            productId = product.IdProducto;
            await InventarioTestSetup.PrepararProductoAsync(setup, productId);
        }
        using var cases = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "pedido-notas.json")));
        foreach (var item in cases.RootElement.EnumerateArray())
        {
            var text = string.Concat(Enumerable.Repeat(item.GetProperty("texto").GetString()!, item.GetProperty("repeticiones").GetInt32()));
            var valid = item.GetProperty("valida").GetBoolean();
            await using var context = _database.CreateContext();
            var version = await context.Mesas.Where(x => x.IdMesa == 1).Select(x => x.Version).SingleAsync();
            var result = await new EnviosService(context, new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System)
                .EnviarAsync(new EnvioRequest(Guid.NewGuid(), 1, null, version,
                    [new EnvioLineaRequest(productId, 1, 1, text)]), 1, CancellationToken.None);
            Assert.Equal(valid ? 201 : 400, result.Status);
            if (valid)
            {
                var note = await context.DetallesPedido.OrderByDescending(x => x.IdDetalle).Select(x => x.Notas).FirstAsync();
                Assert.Equal(item.TryGetProperty("normalizada", out var expected) ? expected.GetString() : text, note);
            }
        }
    }

    [Fact]
    public async Task Mixed_catalog_failure_is_atomic_and_success_audit_excludes_free_text()
    {
        int id;
        uint version;
        await using (var setup = _database.CreateContext())
        {
            var product = new Producto { Nombre = "Nombre de producto privado", Categoria = "Comida", Precio = 10.10m, Disponible = true };
            setup.Productos.Add(product);
            await setup.SaveChangesAsync();
            id = product.IdProducto;
            await InventarioTestSetup.PrepararProductoAsync(setup, id);
            version = await setup.Mesas.Where(x => x.IdMesa == 1).Select(x => x.Version).SingleAsync();
        }
        var key = Guid.NewGuid();
        await using (var context = _database.CreateContext())
        {
            var service = new EnviosService(context, new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
            var result = await service.EnviarAsync(new EnvioRequest(key, 1, null, version,
                [new EnvioLineaRequest(id, 1, 2, "Nota privada"), new EnvioLineaRequest(int.MaxValue, 1, 1, "")]),
                1, CancellationToken.None);
            Assert.Equal("CATALOGO_CAMBIADO", result.Code);
        }
        await using (var verify = _database.CreateContext())
        {
            Assert.Empty(await verify.Pedidos.ToListAsync());
            Assert.Empty(await verify.Comandas.ToListAsync());
            Assert.Empty(await verify.DetallesPedido.ToListAsync());
            Assert.Empty(await verify.EnviosPedido.ToListAsync());
            Assert.Empty(await verify.EventosAuditoria.Where(x => x.TipoEvento == "ComandaEnviada").ToListAsync());
            Assert.Equal("Libre", await verify.Mesas.Where(x => x.IdMesa == 1).Select(x => x.Estado).SingleAsync());
        }
        await using (var context = _database.CreateContext())
        {
            var service = new EnviosService(context, new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
            var result = await service.EnviarAsync(new EnvioRequest(key, 1, null, version,
                [new EnvioLineaRequest(id, 1, 2, "Nota privada")]), 1, CancellationToken.None);
            Assert.Equal(201, result.Status);
        }
        await using (var verify = _database.CreateContext())
        {
            var order = await verify.Pedidos.SingleAsync();
            Assert.Equal(20.20m, order.Subtotal);
            Assert.Equal(0m, order.Impuestos);
            Assert.Equal(order.Subtotal, order.Total);
            var audit = await verify.EventosAuditoria.SingleAsync(x => x.TipoEvento == "ComandaEnviada");
            Assert.Equal(1, audit.IdUsuario);
            Assert.DoesNotContain("Nota privada", audit.Detalles, StringComparison.Ordinal);
            Assert.DoesNotContain("Nombre de producto privado", audit.Detalles, StringComparison.Ordinal);
            using var details = JsonDocument.Parse(audit.Detalles);
            Assert.Equal(1, details.RootElement.GetProperty("cantidadLineas").GetInt32());
            Assert.Equal(key, details.RootElement.GetProperty("envioId").GetGuid());
        }
    }

    [Fact]
    public async Task Invalid_quantities_notes_and_unavailable_products_never_create_orders()
    {
        int id;
        uint version;
        await using (var setup = _database.CreateContext())
        {
            var product = new Producto { Nombre = "Prueba", Categoria = "Bebida", Precio = 0.01m, Disponible = true };
            setup.Productos.Add(product);
            await setup.SaveChangesAsync();
            id = product.IdProducto;
            version = await setup.Mesas.Where(x => x.IdMesa == 1).Select(x => x.Version).SingleAsync();
        }
        await using (var context = _database.CreateContext())
        {
            var service = new EnviosService(context, new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
            foreach (var line in new[]
                     {
                         new EnvioLineaRequest(id, 1, 0, ""),
                         new EnvioLineaRequest(id, 1, -1, ""),
                         new EnvioLineaRequest(id, 1, 100, ""),
                         new EnvioLineaRequest(id, 1, 1, "nota\ninválida"),
                         new EnvioLineaRequest(id, 1, 1, new string('a', 151))
                     })
            {
                Assert.Equal(400, (await service.EnviarAsync(new EnvioRequest(Guid.NewGuid(), 1, null, version, [line]),
                    1, CancellationToken.None)).Status);
            }
            Assert.Equal(400, (await service.EnviarAsync(new EnvioRequest(Guid.NewGuid(), 1, null, version, []),
                1, CancellationToken.None)).Status);
        }
        await using (var update = _database.CreateContext())
        {
            await update.Database.ExecuteSqlInterpolatedAsync($"UPDATE producto SET disponible = false WHERE id_producto = {id}");
        }
        await using (var context = _database.CreateContext())
        {
            var service = new EnviosService(context, new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
            var result = await service.EnviarAsync(new EnvioRequest(Guid.NewGuid(), 1, null, version,
                [new EnvioLineaRequest(id, 2, 1, "")]), 1, CancellationToken.None);
            Assert.Equal("CATALOGO_CAMBIADO", result.Code);
            Assert.Empty(await context.Pedidos.ToListAsync());
            Assert.Empty(await context.EnviosPedido.ToListAsync());
            Assert.Empty(await context.EventosAuditoria.Where(x => x.TipoEvento == "ComandaEnviada").ToListAsync());
        }
    }

    [Fact]
    public async Task Order_limits_unicode_price_history_and_overflow_are_enforced()
    {
        int productId;
        uint version;
        await using (var setup = _database.CreateContext())
        {
            var product = new Producto { Nombre = "Prueba límite", Categoria = "Comida", Precio = 999999.99m, Disponible = true };
            setup.Productos.Add(product);
            await setup.SaveChangesAsync();
            productId = product.IdProducto;
            await InventarioTestSetup.PrepararProductoAsync(setup, productId);
            version = await setup.Mesas.Where(x => x.IdMesa == 1).Select(x => x.Version).SingleAsync();
        }
        var note = string.Concat(Enumerable.Repeat("😀", 150));
        var lines = Enumerable.Range(0, 50).Select(_ => (EnvioLineaRequest?)new EnvioLineaRequest(productId, 1, 99, note)).ToList();
        var request = new EnvioRequest(Guid.NewGuid(), 1, null, version, lines);
        await using (var context = _database.CreateContext())
        {
            var service = new EnviosService(context, new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
            Assert.Equal(201, (await service.EnviarAsync(request, 1, CancellationToken.None)).Status);
        }
        await using (var context = _database.CreateContext())
        {
            version = await context.Mesas.Where(x => x.IdMesa == 1).Select(x => x.Version).SingleAsync();
            var service = new EnviosService(context, new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
            Assert.Equal(400, (await service.EnviarAsync(new EnvioRequest(Guid.NewGuid(), 1, null, version,
                [new EnvioLineaRequest(productId, 1, 100, "")]), 1, CancellationToken.None)).Status);
            Assert.Equal(400, (await service.EnviarAsync(new EnvioRequest(Guid.NewGuid(), 1, null, version,
                [new EnvioLineaRequest(productId, 1, 1, note + "😀")]), 1, CancellationToken.None)).Status);
        }
        await using (var change = _database.CreateContext())
        {
            await change.Database.ExecuteSqlInterpolatedAsync($"UPDATE producto SET precio = {0.01m} WHERE id_producto = {productId}");
        }
        await using (var context = _database.CreateContext())
        {
            var service = new EnviosService(context, new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
            Assert.Equal("CATALOGO_CAMBIADO", (await service.EnviarAsync(new EnvioRequest(Guid.NewGuid(), 1, null, version,
                [new EnvioLineaRequest(productId, 1, 1, "")]), 1, CancellationToken.None)).Code);
            Assert.Equal(201, (await service.EnviarAsync(new EnvioRequest(Guid.NewGuid(), 1, null, version,
                [new EnvioLineaRequest(productId, 2, 1, "")]), 1, CancellationToken.None)).Status);
        }
        await using var verify = _database.CreateContext();
        var account = await verify.Pedidos.SingleAsync();
        Assert.Equal(4949999950.51m, account.Subtotal);
        Assert.Equal(0m, account.Impuestos);
        Assert.Equal(account.Subtotal, account.Total);
        Assert.Equal(50, await verify.DetallesPedido.CountAsync(x => x.PrecioUnitario == 999999.99m));
        Assert.DoesNotContain(await verify.EventosAuditoria.Select(x => x.Detalles).ToListAsync(), x => x.Contains("😀", StringComparison.Ordinal));
        await verify.Database.ExecuteSqlRawAsync("UPDATE pedido SET subtotal = 9999999999.99, total = 9999999999.99 WHERE id_mesa = 1");
        verify.ChangeTracker.Clear();
        var overflow = await new EnviosService(verify, new AuditRecorder(verify, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System)
            .EnviarAsync(new EnvioRequest(Guid.NewGuid(), 1, null, version,
                [new EnvioLineaRequest(productId, 2, 1, "")]), 1, CancellationToken.None);
        Assert.Equal(400, overflow.Status);
        Assert.Equal(2, await verify.Comandas.CountAsync());
    }

    [Fact]
    public async Task Audit_failure_rolls_back_order_table_and_idempotency_record()
    {
        int productId;
        uint version;
        await using (var setup = _database.CreateContext())
        {
            var product = new Producto { Nombre = "Prueba", Categoria = "Comida", Precio = 1m, Disponible = true };
            setup.Productos.Add(product);
            await setup.SaveChangesAsync();
            productId = product.IdProducto;
            await InventarioTestSetup.PrepararProductoAsync(setup, productId);
            version = await setup.Mesas.Where(item => item.IdMesa == 1).Select(item => item.Version).SingleAsync();
        }
        var request = new EnvioRequest(Guid.NewGuid(), 1, null, version,
            [new EnvioLineaRequest(productId, 1, 1, "nota privada")]);
        await using (var context = _database.CreateContext())
        {
            var service = new EnviosService(context, new FailingAuditRecorder(), TimeProvider.System);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnviarAsync(request, 1, CancellationToken.None));
        }
        await using (var verify = _database.CreateContext())
        {
            Assert.Empty(await verify.Pedidos.ToListAsync());
            Assert.Empty(await verify.Comandas.ToListAsync());
            Assert.Empty(await verify.EnviosPedido.ToListAsync());
            Assert.Equal("Libre", await verify.Mesas.Where(item => item.IdMesa == 1).Select(item => item.Estado).SingleAsync());
        }
        await using (var retry = _database.CreateContext())
        {
            var audit = new AuditRecorder(retry, new TestHttpContextAccessor(), TimeProvider.System);
            var result = await new EnviosService(retry, audit, TimeProvider.System).EnviarAsync(request, 1, CancellationToken.None);
            Assert.Equal(201, result.Status);
        }
    }

    private sealed class FailingAuditRecorder : IAuditRecorder
    {
        public void Record(string eventType, string result, int? userId = null, Guid? sessionId = null,
            string? entityType = null, string? entityId = null, IReadOnlyDictionary<string, object?>? details = null) =>
            throw new InvalidOperationException("Injected audit failure");
    }

    [Fact]
    public async Task Migration_preserves_legacy_orders_and_enforces_new_order_constraints()
    {
        int pedidoId;
        int comandaId;
        int productoId;
        var envioId = Guid.NewGuid();

        await using (var context = _database.CreateContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var producto = new Producto
            {
                Nombre = "Producto ficticio de prueba",
                Categoria = "Comida",
                Precio = 10.10m,
                Disponible = true
            };
            var pedido = new Pedido
            {
                IdMesa = 1,
                IdUsuario = 1,
                FechaHora = DateTimeOffset.UtcNow,
                Subtotal = 30.30m,
                Impuestos = 0m,
                Total = 30.30m,
                Estado = "Abierto"
            };
            var comanda = new Comanda
            {
                Pedido = pedido,
                IdUsuario = 1,
                FechaHora = DateTimeOffset.UtcNow,
                Estado = "Abierta"
            };
            comanda.Detalles.Add(new DetallePedido
            {
                Producto = producto,
                NombreProducto = producto.Nombre,
                Cantidad = 2,
                PrecioUnitario = producto.Precio,
                Subtotal = 20.20m,
                Notas = "Sin picante"
            });
            comanda.Detalles.Add(new DetallePedido
            {
                Producto = producto,
                NombreProducto = producto.Nombre,
                Cantidad = 1,
                PrecioUnitario = producto.Precio,
                Subtotal = 10.10m,
                Notas = "Con extra"
            });

            context.Productos.Add(producto);
            context.Pedidos.Add(pedido);
            context.Comandas.Add(comanda);
            await context.SaveChangesAsync();

            pedidoId = pedido.IdPedido;
            comandaId = comanda.IdComanda;
            productoId = producto.IdProducto;
            context.EnviosPedido.Add(new EnvioPedido
            {
                EnvioId = envioId,
                IdUsuario = 1,
                HashSolicitud = new string('a', 64),
                IdComanda = comandaId,
                StatusCode = 201,
                Location = $"/api/cocina/comandas/{comandaId}",
                ResponseJson = JsonSerializer.Serialize(new { idPedido = pedidoId, idComanda = comandaId }),
                FechaHora = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var context = _database.CreateContext())
        {
            Assert.Equal(2, await context.DetallesPedido.CountAsync(detail => detail.IdComanda == comandaId));
            Assert.Equal(30.30m, await context.Pedidos
                .Where(order => order.IdPedido == pedidoId)
                .Select(order => order.Total)
                .SingleAsync());
            Assert.Equal(1, await context.EnviosPedido.CountAsync(send => send.EnvioId == envioId));
            Assert.NotEqual(0u, await context.Comandas
                .Where(command => command.IdComanda == comandaId)
                .Select(command => command.Version)
                .SingleAsync());
            Assert.Equal(1, await context.Productos
                .Where(product => product.IdProducto == productoId)
                .Select(product => product.Version)
                .SingleAsync());

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE producto SET disponible = false WHERE id_producto = {productoId}");
            context.ChangeTracker.Clear();
            Assert.Equal(2, await context.Productos
                .Where(product => product.IdProducto == productoId)
                .Select(product => product.Version)
                .SingleAsync());

            context.Productos.Add(new Producto
            {
                Nombre = "Categoría inválida",
                Categoria = "Postre",
                Precio = 5m,
                Disponible = true
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            context.ChangeTracker.Clear();

            context.DetallesPedido.Add(new DetallePedido
            {
                IdComanda = comandaId,
                IdProducto = productoId,
                NombreProducto = "Producto ficticio de prueba",
                Cantidad = 1,
                PrecioUnitario = 10.10m,
                Subtotal = 9m,
                Notas = string.Empty
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await using (var duplicateContext = _database.CreateContext())
        {
            duplicateContext.EnviosPedido.Add(new EnvioPedido
            {
                EnvioId = envioId,
                IdUsuario = 1,
                HashSolicitud = new string('a', 64),
                IdComanda = comandaId,
                StatusCode = 201,
                Location = $"/api/cocina/comandas/{comandaId}",
                ResponseJson = JsonSerializer.Serialize(new { idPedido = pedidoId, idComanda = comandaId }),
                FechaHora = DateTimeOffset.UtcNow
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        }
    }
}

public sealed class PedidosMigrationCompatibilityTests
{
    [Fact]
    public async Task Upgrade_from_001_preserves_active_legacy_order_without_creating_commands()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync("20261103050000_EnforceAuditAppendOnly");
            int legacyPedidoId;
            await using (var context = database.CreateContext())
            {
                var legacyPedido = new Pedido
                {
                    IdMesa = 1,
                    IdUsuario = 1,
                    FechaHora = DateTimeOffset.UtcNow,
                    Subtotal = 42m,
                    Impuestos = 0m,
                    Total = 42m,
                    Estado = "Listo"
                };
                context.Pedidos.Add(legacyPedido);
                await context.SaveChangesAsync();
                legacyPedidoId = legacyPedido.IdPedido;
                await context.Database.MigrateAsync();
            }

            await using (var context = database.CreateContext())
            {
                var legacyPedido = await context.Pedidos.SingleAsync(order => order.IdPedido == legacyPedidoId);
                Assert.Equal("Listo", legacyPedido.Estado);
                Assert.Equal(42m, legacyPedido.Total);
                Assert.Empty(await context.Comandas.Where(command => command.IdPedido == legacyPedidoId).ToListAsync());

                context.Pedidos.Add(new Pedido
                {
                    IdMesa = 1,
                    IdUsuario = 1,
                    FechaHora = DateTimeOffset.UtcNow,
                    Subtotal = 1m,
                    Impuestos = 0m,
                    Total = 1m,
                    Estado = "Abierto"
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            }
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task Incompatible_existing_catalog_table_aborts_migration_without_partial_schema_changes()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync("20261103050000_EnforceAuditAppendOnly");
            await using (var context = database.CreateContext())
            {
                await context.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE public.producto (legacy_id integer PRIMARY KEY, marker text NOT NULL);
                    INSERT INTO public.producto (legacy_id, marker) VALUES (7, 'preserve');
                    """);

                await Assert.ThrowsAsync<PostgresException>(() => context.Database.MigrateAsync());
            }

            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using (var verifyLegacy = new NpgsqlCommand(
                             "SELECT marker FROM public.producto WHERE legacy_id = 7",
                             connection))
            {
                Assert.Equal("preserve", await verifyLegacy.ExecuteScalarAsync());
            }

            await using (var verifyPartialSchema = new NpgsqlCommand(
                             "SELECT to_regclass('public.comanda') IS NOT NULL",
                             connection))
            {
                Assert.Equal(false, await verifyPartialSchema.ExecuteScalarAsync());
            }
        }
        finally
        {
            await database.DisposeAsync();
        }
    }
}

public sealed class DemoCatalogSeederTests
{
    [Fact]
    public async Task Duplicate_demo_name_with_missing_product_is_rejected_without_changes()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();
            var seeder = CreateSeeder(context, Environments.Development, enabled: true);
            await seeder.SeedAsync(CancellationToken.None);
            await context.Database.ExecuteSqlRawAsync("""
                UPDATE producto SET nombre = 'Gallo pinto DEMO', categoria = 'Comida', precio = 85.00
                WHERE nombre = 'Quesillo DEMO'
                """);
            var before = await context.Productos.AsNoTracking().OrderBy(x => x.IdProducto)
                .Select(x => new { x.IdProducto, x.Nombre, x.Precio, x.Version }).ToListAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync(CancellationToken.None));
            var after = await context.Productos.AsNoTracking().OrderBy(x => x.IdProducto)
                .Select(x => new { x.IdProducto, x.Nombre, x.Precio, x.Version }).ToListAsync();
            Assert.Equal(before, after);
            Assert.Equal(2, after.Count(x => x.Nombre == "Gallo pinto DEMO"));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task Explicit_seed_is_repeatable_and_does_not_overwrite_a_non_demo_catalog()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();
            var seeder = CreateSeeder(context, Environments.Development, enabled: true);

            await seeder.SeedAsync(CancellationToken.None);
            var initial = await context.Productos.AsNoTracking()
                .OrderBy(product => product.Nombre)
                .Select(product => new { product.Nombre, product.Categoria, product.Precio, product.Version })
                .ToListAsync();
            Assert.Equal(10, initial.Count);
            Assert.All(initial, product => Assert.Contains("DEMO", product.Nombre, StringComparison.Ordinal));
            Assert.Equal(new[]
            {
                ("Agua embotellada DEMO", "Bebida", 30m),
                ("Café negro DEMO", "Bebida", 35m),
                ("Cerveza local DEMO", "Licor", 80m),
                ("Gallo pinto DEMO", "Comida", 85m),
                ("Jugo de naranja DEMO", "Bebida", 55m),
                ("Limonada DEMO", "Bebida", 50m),
                ("Nacatamal DEMO", "Comida", 125m),
                ("Quesillo DEMO", "Comida", 95m),
                ("Ron servido DEMO", "Licor", 100m),
                ("Tajadas con queso DEMO", "Comida", 110m)
            }, initial.Select(product => (product.Nombre, product.Categoria, product.Precio)));

            await seeder.SeedAsync(CancellationToken.None);
            var repeated = await context.Productos.AsNoTracking()
                .OrderBy(product => product.Nombre)
                .Select(product => new { product.Nombre, product.Categoria, product.Precio, product.Version })
                .ToListAsync();
            Assert.Equal(initial, repeated);

            context.Productos.Add(new Producto
            {
                Nombre = "Producto operativo no demo",
                Categoria = "Comida",
                Precio = 42m,
                Disponible = true
            });
            await context.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => seeder.SeedAsync(CancellationToken.None));
            Assert.Equal(11, await context.Productos.CountAsync());
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public async Task Seed_requires_development_and_explicit_configuration(string environment, bool enabled)
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();
            var seeder = CreateSeeder(context, environment, enabled);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => seeder.SeedAsync(CancellationToken.None));
            Assert.Empty(await context.Productos.ToListAsync());
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    private static DemoCatalogSeeder CreateSeeder(
        ElCarbonazo.Api.Data.AppDbContext context,
        string environment,
        bool enabled) => new(
        context,
        new TestHostEnvironment(environment),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DemoCatalog:Enabled"] = enabled.ToString()
            })
            .Build());
}

public sealed class RuntimeGrantsTests
{
    [Fact]
    public async Task Runtime_grants_script_applies_least_privilege_to_new_tables_and_sequences()
    {
        var database = new TemporaryPostgresDatabase();
        var runtimeRole = $"ec_test_runtime_{Guid.NewGuid():N}";
        var bootstrapRole = $"ec_test_bootstrap_{Guid.NewGuid():N}";
        var rolesCreated = false;
        try
        {
            await database.InitializeAsync();
            await using var connection = new NpgsqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using (var createRoles = new NpgsqlCommand(
                             $"CREATE ROLE {runtimeRole}; CREATE ROLE {bootstrapRole};",
                             connection))
            {
                await createRoles.ExecuteNonQueryAsync();
            }
            rolesCreated = true;

            var script = await File.ReadAllTextAsync(FindRepositoryFile("deploy", "runtime-grants.sql"));
            script = string.Join('\n', script.Split('\n')
                .Where(line => !line.TrimStart().StartsWith("GRANT CONNECT ON DATABASE", StringComparison.Ordinal)));
            script = script.Replace("elcarbonazo_runtime", runtimeRole, StringComparison.Ordinal)
                .Replace("elcarbonazo_bootstrap", bootstrapRole, StringComparison.Ordinal);
            await using (var applyGrants = new NpgsqlCommand(script, connection))
            {
                await applyGrants.ExecuteNonQueryAsync();
            }

            await using var verify = new NpgsqlCommand("""
                SELECT
                    has_table_privilege(@role, 'public.producto', 'SELECT'),
                    has_table_privilege(@role, 'public.producto', 'INSERT'),
                    has_column_privilege(@role, 'public.pedido', 'estado', 'UPDATE'),
                    has_column_privilege(@role, 'public.pedido', 'fecha_hora', 'UPDATE'),
                    has_column_privilege(@role, 'public.comanda', 'estado', 'UPDATE'),
                    has_column_privilege(@role, 'public.comanda', 'id_pedido', 'UPDATE'),
                    has_table_privilege(@role, 'public.detalle_pedido', 'INSERT'),
                    has_table_privilege(@role, 'public.envio_pedido', 'INSERT'),
                    has_sequence_privilege(@role, 'public.producto_id_producto_seq', 'USAGE'),
                    has_table_privilege(@role, 'public.evento_auditoria', 'DELETE'),
                    has_table_privilege(@role, 'public.pedido', 'INSERT'),
                    has_sequence_privilege(@role, 'public.pedido_id_pedido_seq', 'USAGE'),
                    has_table_privilege(@role, 'public.operacion_caja', 'SELECT'),
                    has_column_privilege(@role, 'public.operacion_caja', 'estado', 'UPDATE'),
                    has_column_privilege(@role, 'public.operacion_caja', 'tipo', 'UPDATE'),
                    has_table_privilege(@role, 'public.operacion_caja', 'DELETE'),
                    has_column_privilege(@role, 'public.turno_caja', 'estado', 'UPDATE'),
                    has_column_privilege(@role, 'public.turno_caja', 'fondo_inicial', 'UPDATE'),
                    has_table_privilege(@role, 'public.cobro', 'INSERT'),
                    has_table_privilege(@role, 'public.cobro', 'UPDATE'),
                    has_table_privilege(@role, 'public.comprobante_cobro_linea', 'INSERT'),
                    has_sequence_privilege(@role, 'public.cobro_numero_comprobante_seq', 'USAGE'),
                    has_sequence_privilege(@role, 'public.comprobante_cobro_linea_id_comprobante_cobro_linea_seq', 'USAGE'),
                    has_schema_privilege(@role, 'public', 'CREATE'),
                    has_table_privilege(@role, 'public.cobro', 'DELETE'),
                    has_table_privilege(@role, 'public.turno_caja', 'DELETE'),
                    (SELECT rolsuper FROM pg_roles WHERE rolname = @role),
                    EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'public' AND c.relname IN ('operacion_caja', 'turno_caja', 'cobro', 'comprobante_cobro_linea')
                          AND pg_get_userbyid(c.relowner) = @role),
                    has_table_privilege(@role, 'public.comprobante_cobro_linea', 'DELETE'),
                    has_table_privilege(@role, 'public.movimiento_inventario', 'INSERT'),
                    has_table_privilege(@role, 'public.movimiento_inventario', 'UPDATE'),
                    has_table_privilege(@role, 'public.movimiento_inventario', 'DELETE'),
                    has_table_privilege(@role, 'public.detalle_receta', 'DELETE'),
                    has_column_privilege(@role, 'public.ingrediente', 'stock_actual', 'UPDATE'),
                    has_sequence_privilege(@role, 'public.movimiento_inventario_id_movimiento_seq', 'USAGE'),
                    has_table_privilege(@role, 'public.evento_auditoria', 'UPDATE'),
                    has_table_privilege(@role, 'public.consumo_detalle', 'INSERT'),
                    has_table_privilege(@role, 'public.consumo_detalle', 'UPDATE'),
                    has_table_privilege(@role, 'public.anulacion_pedido', 'DELETE'),
                    has_column_privilege(@role, 'public.comanda', 'revision_detalles', 'UPDATE'),
                    has_column_privilege(@role, 'public.detalle_pedido', 'cancelado', 'UPDATE'),
                    has_column_privilege(@role, 'public.autorizacion_anulacion', 'hash_token', 'UPDATE'),
                    has_column_privilege(@role, 'public.autorizacion_anulacion', 'fecha_consumo_utc', 'UPDATE'),
                    has_column_privilege(@role, 'public.alerta_anulacion', 'fecha_acuse_utc', 'UPDATE')
                """, connection);
            verify.Parameters.AddWithValue("role", runtimeRole);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetBoolean(0));
            Assert.False(reader.GetBoolean(1));
            Assert.True(reader.GetBoolean(2));
            Assert.False(reader.GetBoolean(3));
            Assert.True(reader.GetBoolean(4));
            Assert.False(reader.GetBoolean(5));
            Assert.True(reader.GetBoolean(6));
            Assert.True(reader.GetBoolean(7));
            Assert.True(reader.GetBoolean(8));
            Assert.False(reader.GetBoolean(9));
            Assert.True(reader.GetBoolean(10));
            Assert.True(reader.GetBoolean(11));
            Assert.True(reader.GetBoolean(12));
            Assert.True(reader.GetBoolean(13));
            Assert.False(reader.GetBoolean(14));
            Assert.False(reader.GetBoolean(15));
            Assert.True(reader.GetBoolean(16));
            Assert.False(reader.GetBoolean(17));
            Assert.True(reader.GetBoolean(18));
            Assert.False(reader.GetBoolean(19));
            Assert.True(reader.GetBoolean(20));
            Assert.True(reader.GetBoolean(21));
            Assert.True(reader.GetBoolean(22));
            Assert.False(reader.GetBoolean(23));
            Assert.False(reader.GetBoolean(24));
            Assert.False(reader.GetBoolean(25));
            Assert.False(reader.GetBoolean(26));
            Assert.False(reader.GetBoolean(27));
            Assert.False(reader.GetBoolean(28));
            Assert.True(reader.GetBoolean(29));
            Assert.False(reader.GetBoolean(30));
            Assert.False(reader.GetBoolean(31));
            Assert.True(reader.GetBoolean(32));
            Assert.True(reader.GetBoolean(33));
            Assert.True(reader.GetBoolean(34));
            Assert.False(reader.GetBoolean(35));
            Assert.True(reader.GetBoolean(36));
            Assert.False(reader.GetBoolean(37));
            Assert.False(reader.GetBoolean(38));
            Assert.True(reader.GetBoolean(39));
            Assert.True(reader.GetBoolean(40));
            Assert.False(reader.GetBoolean(41));
            Assert.True(reader.GetBoolean(42));
            Assert.True(reader.GetBoolean(43));
            await reader.CloseAsync();

            await using (var seed = new NpgsqlCommand("""
                INSERT INTO producto (nombre, categoria, precio, disponible)
                VALUES ('Producto de prueba', 'Comida', 10, true)
                """, connection))
            {
                await seed.ExecuteNonQueryAsync();
            }
            var setupOptions = new DbContextOptionsBuilder<ElCarbonazo.Api.Data.AppDbContext>().UseNpgsql(connection).Options;
            await using (var inventorySetup = new ElCarbonazo.Api.Data.AppDbContext(setupOptions))
            {
                var productId = await inventorySetup.Productos.Select(x => x.IdProducto).SingleAsync();
                await InventarioTestSetup.PrepararProductoAsync(inventorySetup, productId);
            }
            await using (var setRole = new NpgsqlCommand($"SET ROLE {runtimeRole}", connection))
            {
                await setRole.ExecuteNonQueryAsync();
            }
            foreach (var sql in new[]
            {
                "UPDATE estado_hmac_autorizacion SET provisionado = true WHERE id = 1",
                "DELETE FROM estado_hmac_autorizacion WHERE id = 1",
                "INSERT INTO estado_hmac_autorizacion (id) VALUES (1)"
            })
            {
                await using var forbiddenProvisioning = new NpgsqlCommand(sql, connection);
                var error = await Assert.ThrowsAsync<PostgresException>(() => forbiddenProvisioning.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            }
            var options = new DbContextOptionsBuilder<ElCarbonazo.Api.Data.AppDbContext>()
                .UseNpgsql(connection).Options;
            await using (var hmacContext = new ElCarbonazo.Api.Data.AppDbContext(options))
            {
                var guard = new HmacAutorizacionGuard(hmacContext, TimeProvider.System);
                var called = false;
                var state = await guard.EjecutarAsync(new byte[32], _ => { called = true; return Task.CompletedTask; }, default);
                Assert.Equal(EstadoHmacAutorizacion.NoProvisionado, state);
                Assert.False(called);
                var provisioner = new HmacAutorizacionProvisioner(hmacContext,
                    new AuditRecorder(hmacContext, new TestHttpContextAccessor(), TimeProvider.System));
                var error = await Assert.ThrowsAsync<PostgresException>(() => provisioner.ProvisionarInicialAsync(new byte[32], default));
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
                var recovery = new HmacAutorizacionRecovery(hmacContext,
                    new AuditRecorder(hmacContext, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
                error = await Assert.ThrowsAsync<PostgresException>(() => recovery.RecuperarAusenteAsync(new byte[32], default));
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            }
            await using (var context = new ElCarbonazo.Api.Data.AppDbContext(options))
            {
                var product = await context.Productos.SingleAsync();
                var table = await context.Mesas.SingleAsync(item => item.IdMesa == 1);
                var audit = new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System);
                var service = new ElCarbonazo.Api.Services.EnviosService(context, audit, TimeProvider.System);
                var result = await service.EnviarAsync(new ElCarbonazo.Api.DTOs.EnvioRequest(
                    Guid.NewGuid(), 1, null, table.Version,
                    [new ElCarbonazo.Api.DTOs.EnvioLineaRequest(product.IdProducto, product.Version, 1, "")]),
                    1, CancellationToken.None);
                Assert.Equal(201, result.Status);
            }
            await using (var kitchenContext = new ElCarbonazo.Api.Data.AppDbContext(options))
            {
                var command = await kitchenContext.Comandas.AsNoTracking().SingleAsync();
                var audit = new AuditRecorder(kitchenContext, new TestHttpContextAccessor(), TimeProvider.System);
                var kitchen = new CocinaService(kitchenContext, audit);
                var result = await kitchen.CambiarEstadoAsync(command.IdComanda,
                    new CambiarComandaEstadoRequest("En Preparación", command.Version), 1, CancellationToken.None);
                Assert.Equal(200, result.Status);
                var ready = await kitchen.CambiarEstadoAsync(command.IdComanda,
                    new CambiarComandaEstadoRequest("Lista", result.Comanda!.Version), 1, CancellationToken.None);
                Assert.Equal(200, ready.Status);
            }
            await using (var cashContext = new ElCarbonazo.Api.Data.AppDbContext(options))
            {
                var audit = new AuditRecorder(cashContext, new TestHttpContextAccessor(), TimeProvider.System);
                var opening = new CajaOperacionesService(cashContext, audit, TimeProvider.System,
                    new CajaAperturaExecutor(cashContext, audit, TimeProvider.System));
                var openingId = Guid.NewGuid();
                Assert.Equal(200, (await opening.PrepararAsync(openingId, 1, CajaOperacionCanonica.Abrir(0, true), default)).Status);
                Assert.Equal(201, (await opening.EjecutarAsync(openingId, 1, default)).Status);
                var shiftId = await cashContext.TurnosCaja.Select(x => x.IdTurnoCaja).SingleAsync();
                var order = await cashContext.Pedidos.AsNoTracking()
                    .Include(x => x.Comandas).ThenInclude(x => x.Detalles).SingleAsync();
                var fingerprint = CajaElegibilidad.Evaluar(order).HuellaLineas;
                Assert.NotNull(fingerprint);
                var payment = new CajaOperacionesService(cashContext, audit, TimeProvider.System,
                    new CajaCobroExecutor(cashContext, audit, TimeProvider.System));
                var paymentId = Guid.NewGuid();
                var intent = CajaOperacionCanonica.Cobrar(order.IdPedido, shiftId, order.Total, fingerprint, CajaStates.Efectivo, 10m, null, false, false);
                Assert.Equal(200, (await payment.PrepararAsync(paymentId, 1, intent, default)).Status);
                Assert.Equal(201, (await payment.EjecutarAsync(paymentId, 1, default)).Status);
                Assert.Equal(PedidoStates.Pagado, await cashContext.Pedidos.AsNoTracking().Select(x => x.Estado).SingleAsync());
                Assert.Equal(1, await cashContext.Cobros.CountAsync());
                var closing = new CajaOperacionesService(cashContext, audit, TimeProvider.System,
                    new CajaCierreExecutor(cashContext, audit, TimeProvider.System));
                var closingId = Guid.NewGuid();
                Assert.Equal(200, (await closing.PrepararAsync(closingId, 1, CajaOperacionCanonica.Cerrar(shiftId, 10m, null, true), default)).Status);
                Assert.Equal(200, (await closing.EjecutarAsync(closingId, 1, default)).Status);
            }
            await using (var inventory = new NpgsqlCommand("""
                BEGIN;
                SELECT id_producto FROM producto ORDER BY id_producto FOR UPDATE;
                SELECT id_ingrediente FROM ingrediente ORDER BY id_ingrediente FOR UPDATE;
                UPDATE ingrediente SET stock_actual = stock_actual + 2.5000, version = version + 1, fecha_actualizacion_utc = now();
                UPDATE receta SET version = version + 1, fecha_actualizacion_utc = now();
                INSERT INTO movimiento_inventario
                    (id_ingrediente, tipo_movimiento, cantidad_delta, stock_anterior, stock_posterior, id_usuario, fecha_utc)
                SELECT id_ingrediente, 'EntradaCompra', 2.5000, stock_actual - 2.5000, stock_actual, 1, now() FROM ingrediente;
                DELETE FROM detalle_receta;
                INSERT INTO detalle_receta (id_receta, id_ingrediente, cantidad_requerida)
                SELECT r.id_receta, i.id_ingrediente, 0.4000 FROM receta r CROSS JOIN ingrediente i;
                COMMIT;
                """, connection))
            {
                await inventory.ExecuteNonQueryAsync();
            }
            // Persistence permissions only: this does not execute or authorize a business cancellation.
            await using (var cancellationPersistence = new NpgsqlCommand("""
                INSERT INTO consumo_detalle (id_detalle, id_ingrediente, cantidad)
                SELECT d.id_detalle, i.id_ingrediente, 0.4 FROM detalle_pedido d CROSS JOIN ingrediente i
                ON CONFLICT (id_detalle, id_ingrediente) DO NOTHING;
                INSERT INTO anulacion_pedido
                    (id_pedido, id_comanda, id_detalle, cantidad_anulada, monto_revertido,
                     estado_comanda_previo, id_usuario_solicita, id_usuario_autoriza, motivo, inventario_revertido, fecha_utc)
                SELECT p.id_pedido, c.id_comanda, d.id_detalle, 1, 10, 'Lista', 1, 1, 'Prueba ficticia', true, now()
                FROM pedido p JOIN comanda c ON c.id_pedido = p.id_pedido JOIN detalle_pedido d ON d.id_comanda = c.id_comanda;
                INSERT INTO alerta_anulacion (id_anulacion)
                SELECT id_anulacion FROM anulacion_pedido;
                UPDATE alerta_anulacion SET fecha_acuse_utc = now(), id_usuario_acuse = 1;
                INSERT INTO movimiento_inventario
                    (id_ingrediente, tipo_movimiento, cantidad_delta, stock_anterior, stock_posterior,
                     id_pedido, id_comanda, id_anulacion, id_usuario, motivo, fecha_utc)
                SELECT i.id_ingrediente, 'ReversionCancelacion', 0.4, i.stock_actual, i.stock_actual + 0.4,
                    a.id_pedido, a.id_comanda, a.id_anulacion, 1, 'Prueba ficticia', now()
                FROM ingrediente i CROSS JOIN anulacion_pedido a;
                UPDATE ingrediente SET stock_actual = stock_actual + 0.4, version = version + 1, fecha_actualizacion_utc = now();
                UPDATE comanda SET revision_detalles = revision_detalles + 1;
                UPDATE detalle_pedido SET cancelado = true;
                """, connection))
            {
                await cancellationPersistence.ExecuteNonQueryAsync();
            }
            await using (var secondAck = new NpgsqlCommand("UPDATE alerta_anulacion SET fecha_acuse_utc = now()", connection))
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => secondAck.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            }
            await using (var invalidReason = new NpgsqlCommand("""
                INSERT INTO anulacion_pedido
                    (id_pedido, id_comanda, id_detalle, cantidad_anulada, monto_revertido,
                     estado_comanda_previo, id_usuario_solicita, id_usuario_autoriza, motivo, inventario_revertido, fecha_utc)
                SELECT id_pedido, id_comanda, id_detalle, 1, 10, 'Lista', 1, 1,
                    E'No\tpermitido', true, now() FROM anulacion_pedido
                """, connection))
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => invalidReason.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            }
            await using (var duplicate = new NpgsqlCommand("""
                INSERT INTO movimiento_inventario
                    (id_ingrediente, tipo_movimiento, cantidad_delta, stock_anterior, stock_posterior,
                     id_pedido, id_comanda, id_anulacion, id_usuario, motivo, fecha_utc)
                SELECT id_ingrediente, tipo_movimiento, cantidad_delta, stock_anterior, stock_posterior,
                    id_pedido, id_comanda, id_anulacion, id_usuario, motivo, now()
                FROM movimiento_inventario WHERE tipo_movimiento = 'ReversionCancelacion'
                """, connection))
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
            }
            await using (var duplicateSnapshot = new NpgsqlCommand("""
                INSERT INTO consumo_detalle (id_detalle, id_ingrediente, cantidad)
                SELECT id_detalle, id_ingrediente, cantidad FROM consumo_detalle
                """, connection))
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => duplicateSnapshot.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
            }
            foreach (var sql in new[] { "UPDATE consumo_detalle SET cantidad = 2", "DELETE FROM anulacion_pedido",
                         "UPDATE evento_auditoria SET resultado = 'Alterado'", "DELETE FROM evento_auditoria" })
            {
                await using var forbiddenEvidence = new NpgsqlCommand(sql, connection);
                var error = await Assert.ThrowsAsync<PostgresException>(() => forbiddenEvidence.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            }
            await using (var forbidden = new NpgsqlCommand("DELETE FROM movimiento_inventario", connection))
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => forbidden.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            }
            await using (var forbidden = new NpgsqlCommand("UPDATE movimiento_inventario SET cantidad_delta = 3", connection))
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => forbidden.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            }
            await using (var verifyInventory = new NpgsqlCommand("""
                SELECT stock_actual, version, (SELECT count(*) FROM movimiento_inventario),
                    (SELECT cantidad_requerida FROM detalle_receta)
                FROM ingrediente
                """, connection))
            await using (var inventoryReader = await verifyInventory.ExecuteReaderAsync())
            {
                Assert.True(await inventoryReader.ReadAsync());
                Assert.Equal(100000.9000m, inventoryReader.GetDecimal(0));
                Assert.Equal(5L, inventoryReader.GetInt64(1));
                Assert.Equal(4L, inventoryReader.GetInt64(2));
                Assert.Equal(0.4000m, inventoryReader.GetDecimal(3));
            }
            await using (var resetRole = new NpgsqlCommand("RESET ROLE", connection))
            {
                await resetRole.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            if (rolesCreated)
            {
                await using var cleanup = new NpgsqlConnection(database.ConnectionString);
                await cleanup.OpenAsync();
                await using var dropRoles = new NpgsqlCommand(
                    $"DROP OWNED BY {runtimeRole}; DROP OWNED BY {bootstrapRole}; DROP ROLE {runtimeRole}; DROP ROLE {bootstrapRole};",
                    cleanup);
                await dropRoles.ExecuteNonQueryAsync();
            }

            await database.DisposeAsync();
        }
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine([directory.FullName, .. segments]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException($"No se encontró el archivo {Path.Combine(segments)}.");
    }
}
