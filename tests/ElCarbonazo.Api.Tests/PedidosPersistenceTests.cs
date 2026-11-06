using System.Text.Json;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
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
                    has_table_privilege(@role, 'public.evento_auditoria', 'DELETE')
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
