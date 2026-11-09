using System.Text.Json;
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
                    has_sequence_privilege(@role, 'public.pedido_id_pedido_seq', 'USAGE')
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
            await reader.CloseAsync();

            await using (var seed = new NpgsqlCommand("""
                INSERT INTO producto (nombre, categoria, precio, disponible)
                VALUES ('Producto de prueba', 'Comida', 10, true)
                """, connection))
            {
                await seed.ExecuteNonQueryAsync();
            }
            await using (var setRole = new NpgsqlCommand($"SET ROLE {runtimeRole}", connection))
            {
                await setRole.ExecuteNonQueryAsync();
            }
            var options = new DbContextOptionsBuilder<ElCarbonazo.Api.Data.AppDbContext>()
                .UseNpgsql(connection).Options;
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
