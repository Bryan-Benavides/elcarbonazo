using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class EnviosConcurrencyTests : IAsyncLifetime
{
    private readonly TemporaryPostgresDatabase database = new();

    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task Group_command_transitions_without_mutating_member_tables()
    {
        var (product, _) = await SetupAsync();
        int groupId;
        await using (var context = database.CreateContext())
        {
            var tables = await context.Mesas.Where(x => x.IdMesa == 2 || x.IdMesa == 3).ToListAsync();
            var group = await new GruposMesaService(context, TimeProvider.System, Audit(context)).CreateAsync(
                new CreateGrupoMesaRequest(tables.Select(x => new MesaVersionRequest(x.IdMesa, x.Version)).ToList()),
                1, CancellationToken.None);
            groupId = group.Grupo!.IdGrupo;
        }
        Assert.Equal(201, (await StartSend(new EnvioRequest(Guid.NewGuid(), null, groupId, null,
            [new EnvioLineaRequest(product, 1, 1, "")]))).Status);
        int id;
        uint version;
        await using (var context = database.CreateContext())
        {
            var kitchen = new CocinaService(context, Audit(context));
            var item = Assert.Single((await kitchen.ColaAsync(null, 25, CancellationToken.None)).Pagina!.Items);
            Assert.Equal(groupId, item.IdGrupo);
            Assert.Null(item.IdMesa);
            id = item.IdComanda;
            version = item.Version;
        }
        var prepared = await StartKitchen(id, version, "En Preparación");
        Assert.Equal(200, prepared.Status);
        Assert.Equal(200, (await StartKitchen(id, prepared.Comanda!.Version, "Lista")).Status);
        await using var verify = database.CreateContext();
        Assert.Equal("Listo", await verify.Pedidos.Select(x => x.Estado).SingleAsync());
        Assert.Equal(2, await verify.Mesas.CountAsync(x => x.IdGrupo == groupId && x.Estado == MesaStates.Agrupada));
        Assert.True(await verify.GruposMesa.Where(x => x.IdGrupo == groupId).Select(x => x.Activo).SingleAsync());
    }

    [Fact]
    public async Task Kitchen_fifo_breaks_equal_timestamps_by_id_across_pages()
    {
        var (product, version) = await SetupAsync();
        Assert.Equal(201, (await StartSend(Request(product, version))).Status);
        await using (var context = database.CreateContext())
        {
            version = await context.Mesas.Where(x => x.IdMesa == 1).Select(x => x.Version).SingleAsync();
        }
        Assert.Equal(201, (await StartSend(Request(product, version))).Status);
        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE comanda SET fecha_hora = '2026-01-01T12:00:00Z'");
        }
        await using var read = database.CreateContext();
        var ids = await read.Comandas.OrderBy(x => x.IdComanda).Select(x => x.IdComanda).ToArrayAsync();
        var kitchen = new CocinaService(read, Audit(read));
        var first = await kitchen.ColaAsync(null, 1, CancellationToken.None);
        Assert.Equal(ids[0], Assert.Single(first.Pagina!.Items).IdComanda);
        Assert.NotNull(first.Pagina.CursorSiguiente);
        var second = await kitchen.ColaAsync(first.Pagina.CursorSiguiente, 1, CancellationToken.None);
        Assert.Equal(ids[1], Assert.Single(second.Pagina!.Items).IdComanda);
        Assert.Null(second.Pagina.CursorSiguiente);
    }

    [Fact]
    public async Task Two_kitchens_serialize_and_reject_stale_version()
    {
        var (product, tableVersion) = await SetupAsync();
        Assert.Equal(201, (await StartSend(Request(product, tableVersion))).Status);
        int id;
        uint version;
        await using (var context = database.CreateContext())
        {
            var command = await context.Comandas.SingleAsync();
            id = command.IdComanda;
            version = command.Version;
        }
        using var barrier = new AuditBarrier();
        var first = StartKitchen(id, version, "En Preparación", barrier);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var name = $"d3_wait_{Guid.NewGuid():N}";
            var second = StartKitchen(id, version, "En Preparación", null, name);
            await WaitForDatabaseBlockAsync(name);
            barrier.Release.Set();
            Assert.Equal(200, (await first.WaitAsync(TimeSpan.FromSeconds(20))).Status);
            Assert.Equal("VERSION_CONFLICT", (await second.WaitAsync(TimeSpan.FromSeconds(20))).Code);
            await using var verify = database.CreateContext();
            Assert.Equal("En Preparación", await verify.Pedidos.Select(x => x.Estado).SingleAsync());
            Assert.Equal(1, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "ComandaEstadoCambiado"));
        }
        finally
        {
            barrier.Release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    [Fact]
    public async Task New_send_waits_for_kitchen_and_reopens_account_without_changing_history()
    {
        var (product, tableVersion) = await SetupAsync();
        Assert.Equal(201, (await StartSend(Request(product, tableVersion))).Status);
        int id;
        uint commandVersion;
        await using (var context = database.CreateContext())
        {
            var command = await context.Comandas.SingleAsync();
            id = command.IdComanda;
            commandVersion = command.Version;
            tableVersion = await context.Mesas.Where(x => x.IdMesa == 1).Select(x => x.Version).SingleAsync();
        }
        var prepared = await StartKitchen(id, commandVersion, "En Preparación");
        Assert.Equal(200, prepared.Status);
        using var barrier = new AuditBarrier();
        var ready = StartKitchen(id, prepared.Comanda!.Version, "Lista", barrier);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var name = $"d3_wait_{Guid.NewGuid():N}";
            var send = StartSend(Request(product, tableVersion), null, name);
            await WaitForDatabaseBlockAsync(name);
            barrier.Release.Set();
            Assert.Equal(200, (await ready.WaitAsync(TimeSpan.FromSeconds(20))).Status);
            Assert.Equal(201, (await send.WaitAsync(TimeSpan.FromSeconds(20))).Status);
            await using var verify = database.CreateContext();
            Assert.Equal("Abierto", await verify.Pedidos.Select(x => x.Estado).SingleAsync());
            Assert.Equal("Lista", await verify.Comandas.Where(x => x.IdComanda == id).Select(x => x.Estado).SingleAsync());
            Assert.Equal(2, await verify.Comandas.CountAsync());
            Assert.Equal(2, await verify.DetallesPedido.CountAsync());
        }
        finally
        {
            barrier.Release.Set();
            await ready.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    [Fact]
    public async Task Kitchen_waits_for_new_send_and_recalculates_with_new_open_command()
    {
        var (product, tableVersion) = await SetupAsync();
        Assert.Equal(201, (await StartSend(Request(product, tableVersion))).Status);
        int id;
        uint commandVersion;
        await using (var context = database.CreateContext())
        {
            var command = await context.Comandas.SingleAsync();
            id = command.IdComanda;
            commandVersion = command.Version;
            tableVersion = await context.Mesas.Where(x => x.IdMesa == 1).Select(x => x.Version).SingleAsync();
        }
        var prepared = await StartKitchen(id, commandVersion, "En Preparación");
        Assert.Equal(200, prepared.Status);
        using var barrier = new AuditBarrier();
        var send = StartSend(Request(product, tableVersion), barrier);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var name = $"d3_wait_{Guid.NewGuid():N}";
            var ready = StartKitchen(id, prepared.Comanda!.Version, "Lista", null, name);
            await WaitForDatabaseBlockAsync(name);
            barrier.Release.Set();
            Assert.Equal(201, (await send.WaitAsync(TimeSpan.FromSeconds(20))).Status);
            Assert.Equal(200, (await ready.WaitAsync(TimeSpan.FromSeconds(20))).Status);
            await using var verify = database.CreateContext();
            Assert.Equal("Abierto", await verify.Pedidos.Select(x => x.Estado).SingleAsync());
            Assert.Equal(1, await verify.Comandas.CountAsync(x => x.Estado == "Abierta"));
            Assert.Equal(1, await verify.Comandas.CountAsync(x => x.Estado == "Lista"));
        }
        finally
        {
            barrier.Release.Set();
            await send.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    [Fact]
    public async Task Kitchen_audit_failure_rolls_back_command_and_account()
    {
        var (product, tableVersion) = await SetupAsync();
        Assert.Equal(201, (await StartSend(Request(product, tableVersion))).Status);
        await using (var context = database.CreateContext())
        {
            var command = await context.Comandas.AsNoTracking().SingleAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new CocinaService(context, new FailingAudit()).CambiarEstadoAsync(command.IdComanda,
                    new CambiarComandaEstadoRequest("En Preparación", command.Version), 1, CancellationToken.None));
        }
        await using var verify = database.CreateContext();
        Assert.Equal("Abierta", await verify.Comandas.Select(x => x.Estado).SingleAsync());
        Assert.Equal("Abierto", await verify.Pedidos.Select(x => x.Estado).SingleAsync());
        Assert.Empty(await verify.EventosAuditoria.Where(x => x.TipoEvento == "ComandaEstadoCambiado").ToListAsync());
    }

    [Fact]
    public async Task Concurrent_price_change_is_revalidated_after_product_lock()
    {
        var (product, version) = await SetupAsync();
        await using var writer = new NpgsqlConnection(database.ConnectionString);
        await writer.OpenAsync();
        await using var transaction = await writer.BeginTransactionAsync();
        await using (var update = new NpgsqlCommand("UPDATE producto SET precio = 12 WHERE id_producto = @id", writer, transaction))
        {
            update.Parameters.AddWithValue("id", product);
            await update.ExecuteNonQueryAsync();
        }
        var name = $"d2_wait_{Guid.NewGuid():N}";
        var send = StartSend(Request(product, version), null, name);
        await WaitForDatabaseBlockAsync(name);
        await transaction.CommitAsync();
        Assert.Equal("CATALOGO_CAMBIADO", (await send.WaitAsync(TimeSpan.FromSeconds(20))).Code);
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.Pedidos.ToListAsync());
        Assert.Empty(await verify.Comandas.ToListAsync());
        Assert.Equal(MesaStates.Libre, await verify.Mesas.Where(x => x.IdMesa == 1).Select(x => x.Estado).SingleAsync());
    }

    [Fact]
    public async Task Same_key_with_another_actor_never_reveals_original_response()
    {
        var (product, version) = await SetupAsync();
        var request = Request(product, version);
        Assert.Equal(201, (await StartSend(request)).Status);
        await using var context = database.CreateContext();
        var result = await new EnviosService(context, Audit(context), TimeProvider.System)
            .EnviarAsync(request, 2, CancellationToken.None);
        Assert.Equal(409, result.Status);
        Assert.Equal("ENVIO_ID_REUTILIZADO", result.Code);
        Assert.Null(result.Json);
        Assert.Equal(1, await context.Comandas.CountAsync());
    }

    [Fact]
    public async Task Identical_intentions_wait_on_advisory_lock_and_replay_committed_result()
    {
        var (product, version) = await SetupAsync();
        var request = Request(product, version);
        using var barrier = new AuditBarrier();
        var first = StartSend(request, barrier);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var name = $"d2_wait_{Guid.NewGuid():N}";
            var second = StartSend(request, null, name);
            await WaitForDatabaseBlockAsync(name);
            barrier.Release.Set();
            var original = await first.WaitAsync(TimeSpan.FromSeconds(20));
            var replay = await second.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(201, original.Status);
            Assert.Equal(original.Status, replay.Status);
            Assert.Equal(original.Location, replay.Location);
            using var firstJson = System.Text.Json.JsonDocument.Parse(original.Json!);
            using var secondJson = System.Text.Json.JsonDocument.Parse(replay.Json!);
            Assert.True(System.Text.Json.JsonElement.DeepEquals(firstJson.RootElement, secondJson.RootElement));
            await using var verify = database.CreateContext();
            Assert.Equal(1, await verify.Comandas.CountAsync());
            Assert.Equal(1, await verify.EnviosPedido.CountAsync());
        }
        finally
        {
            barrier.Release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    [Fact]
    public async Task Concurrent_openings_serialize_and_stale_table_version_conflicts()
    {
        var (product, version) = await SetupAsync();
        var firstRequest = Request(product, version);
        var secondRequest = Request(product, version);
        using var barrier = new AuditBarrier();
        var first = StartSend(firstRequest, barrier);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var name = $"d2_wait_{Guid.NewGuid():N}";
            var second = StartSend(secondRequest, null, name);
            await WaitForDatabaseBlockAsync(name);
            barrier.Release.Set();
            Assert.Equal(201, (await first.WaitAsync(TimeSpan.FromSeconds(20))).Status);
            var conflict = await second.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal("MESA_CONFLICTO_CONCURRENCIA", conflict.Code);
            await using var verify = database.CreateContext();
            Assert.Equal(1, await verify.Pedidos.CountAsync());
            Assert.Equal(1, await verify.Comandas.CountAsync());
            Assert.Equal(1, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "ComandaEnviada"));
        }
        finally
        {
            barrier.Release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    [Fact]
    public async Task Release_waits_for_send_and_then_observes_pending_account()
    {
        var (product, version) = await SetupAsync();
        Assert.Equal(201, (await StartSend(Request(product, version))).Status);
        await using (var current = database.CreateContext())
        {
            version = await current.Mesas.Where(x => x.IdMesa == 1).Select(x => x.Version).SingleAsync();
        }

        using var barrier = new AuditBarrier();
        var send = StartSend(Request(product, version), barrier);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var name = $"d2_wait_{Guid.NewGuid():N}";
            var release = ReleaseAsync(version, name);
            await WaitForDatabaseBlockAsync(name);
            barrier.Release.Set();
            Assert.Equal(201, (await send.WaitAsync(TimeSpan.FromSeconds(20))).Status);
            Assert.Equal(UpdateMesaEstadoOutcome.PendingOrder, (await release.WaitAsync(TimeSpan.FromSeconds(20))).Outcome);
            await using var verify = database.CreateContext();
            Assert.Equal(2, await verify.Comandas.CountAsync());
            Assert.Equal(MesaStates.Ocupada, await verify.Mesas.Where(x => x.IdMesa == 1).Select(x => x.Estado).SingleAsync());
        }
        finally
        {
            barrier.Release.Set();
            await send.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    [Fact]
    public async Task Group_close_waits_for_send_and_then_observes_pending_account()
    {
        var (product, _) = await SetupAsync();
        int groupId;
        await using (var context = database.CreateContext())
        {
            var tables = await context.Mesas.Where(x => x.IdMesa == 2 || x.IdMesa == 3).ToListAsync();
            var groups = new GruposMesaService(context, TimeProvider.System, Audit(context));
            var group = await groups.CreateAsync(new CreateGrupoMesaRequest(tables.Select(x =>
                new MesaVersionRequest(x.IdMesa, x.Version)).ToList()), 1, CancellationToken.None);
            Assert.Equal(GrupoMesaOutcome.Success, group.Outcome);
            groupId = group.Grupo!.IdGrupo;
        }
        var request = new EnvioRequest(Guid.NewGuid(), null, groupId, null,
            [new EnvioLineaRequest(product, 1, 1, "")]);
        using var barrier = new AuditBarrier();
        var send = StartSend(request, barrier);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var name = $"d2_wait_{Guid.NewGuid():N}";
            var close = CloseAsync(groupId, name);
            await WaitForDatabaseBlockAsync(name);
            barrier.Release.Set();
            Assert.Equal(201, (await send.WaitAsync(TimeSpan.FromSeconds(20))).Status);
            Assert.Equal(GrupoMesaOutcome.PendingOrders, (await close.WaitAsync(TimeSpan.FromSeconds(20))).Outcome);
            await using var verify = database.CreateContext();
            Assert.True(await verify.GruposMesa.Where(x => x.IdGrupo == groupId).Select(x => x.Activo).SingleAsync());
            Assert.Equal(2, await verify.Mesas.CountAsync(x => x.IdGrupo == groupId && x.Estado == MesaStates.Agrupada));
        }
        finally
        {
            barrier.Release.Set();
            await send.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    [Fact]
    public async Task Send_waits_for_group_close_and_cannot_open_in_closed_group()
    {
        var (product, _) = await SetupAsync();
        int groupId;
        await using (var context = database.CreateContext())
        {
            var tables = await context.Mesas.Where(x => x.IdMesa == 2 || x.IdMesa == 3).ToListAsync();
            var group = await new GruposMesaService(context, TimeProvider.System, Audit(context)).CreateAsync(
                new CreateGrupoMesaRequest(tables.Select(x => new MesaVersionRequest(x.IdMesa, x.Version)).ToList()),
                1, CancellationToken.None);
            groupId = group.Grupo!.IdGrupo;
        }
        using var barrier = new AuditBarrier();
        var close = Task.Run(async () =>
        {
            await using var context = CreateContext(null);
            return await new GruposMesaService(context, TimeProvider.System, new BlockingAudit(Audit(context), barrier))
                .CloseAsync(groupId, 1, CancellationToken.None);
        });
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var name = $"d2_wait_{Guid.NewGuid():N}";
            var send = StartSend(new EnvioRequest(Guid.NewGuid(), null, groupId, null,
                [new EnvioLineaRequest(product, 1, 1, "")]), null, name);
            await WaitForDatabaseBlockAsync(name);
            barrier.Release.Set();
            Assert.Equal(GrupoMesaOutcome.Success, (await close.WaitAsync(TimeSpan.FromSeconds(20))).Outcome);
            Assert.Equal(404, (await send.WaitAsync(TimeSpan.FromSeconds(20))).Status);
            await using var verify = database.CreateContext();
            Assert.Empty(await verify.Pedidos.ToListAsync());
            Assert.Empty(await verify.Comandas.ToListAsync());
        }
        finally
        {
            barrier.Release.Set();
            await close.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    [Fact]
    public async Task Send_waits_for_release_and_rejects_stale_table_version()
    {
        var (product, _) = await SetupAsync();
        uint version;
        await using (var context = database.CreateContext())
        {
            var table = await context.Mesas.SingleAsync(x => x.IdMesa == 1);
            table.Estado = MesaStates.Ocupada;
            await context.SaveChangesAsync();
            version = table.Version;
        }
        using var barrier = new AuditBarrier();
        var release = Task.Run(async () =>
        {
            await using var context = CreateContext(null);
            return await new MesasService(context, new BlockingAudit(Audit(context), barrier))
                .UpdateEstadoAsync(1, new UpdateMesaEstadoRequest(MesaStates.Libre, version), CancellationToken.None);
        });
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var name = $"d2_wait_{Guid.NewGuid():N}";
            var send = StartSend(Request(product, version), null, name);
            await WaitForDatabaseBlockAsync(name);
            barrier.Release.Set();
            Assert.Equal(UpdateMesaEstadoOutcome.Success, (await release.WaitAsync(TimeSpan.FromSeconds(20))).Outcome);
            Assert.Equal("MESA_CONFLICTO_CONCURRENCIA", (await send.WaitAsync(TimeSpan.FromSeconds(20))).Code);
            await using var verify = database.CreateContext();
            Assert.Empty(await verify.Pedidos.ToListAsync());
            Assert.Empty(await verify.Comandas.ToListAsync());
        }
        finally
        {
            barrier.Release.Set();
            await release.WaitAsync(TimeSpan.FromSeconds(20));
        }
    }

    private async Task<(int Product, uint Version)> SetupAsync()
    {
        await using var context = database.CreateContext();
        var product = new Producto { Nombre = "Prueba", Categoria = "Comida", Precio = 10m, Disponible = true };
        context.Productos.Add(product);
        await context.SaveChangesAsync();
        return (product.IdProducto, await context.Mesas.Where(x => x.IdMesa == 1).Select(x => x.Version).SingleAsync());
    }

    private static EnvioRequest Request(int product, uint version) => new(Guid.NewGuid(), 1, null, version,
        [new EnvioLineaRequest(product, 1, 1, "")]);

    private Task<EnvioResult> StartSend(EnvioRequest request, AuditBarrier? barrier = null, string? name = null) => Task.Run(async () =>
    {
        await using var context = CreateContext(name);
        IAuditRecorder audit = Audit(context);
        if (barrier is not null) audit = new BlockingAudit(audit, barrier);
        return await new EnviosService(context, audit, TimeProvider.System).EnviarAsync(request, 1, CancellationToken.None);
    });

    private Task<CocinaResult> StartKitchen(int id, uint version, string state, AuditBarrier? barrier = null, string? name = null) => Task.Run(async () =>
    {
        await using var context = CreateContext(name);
        IAuditRecorder audit = Audit(context);
        if (barrier is not null) audit = new BlockingAudit(audit, barrier);
        return await new CocinaService(context, audit).CambiarEstadoAsync(id,
            new CambiarComandaEstadoRequest(state, version), 1, CancellationToken.None);
    });

    private async Task<UpdateMesaEstadoResult> ReleaseAsync(uint version, string name)
    {
        await using var context = CreateContext(name);
        return await new MesasService(context, Audit(context)).UpdateEstadoAsync(1,
            new UpdateMesaEstadoRequest(MesaStates.Libre, version), CancellationToken.None);
    }

    private async Task<GruposMesaResult> CloseAsync(int groupId, string name)
    {
        await using var context = CreateContext(name);
        return await new GruposMesaService(context, TimeProvider.System, Audit(context))
            .CloseAsync(groupId, 1, CancellationToken.None);
    }

    private AppDbContext CreateContext(string? name)
    {
        var connection = new NpgsqlConnectionStringBuilder(database.ConnectionString) { ApplicationName = name ?? "d2_test" };
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options);
    }

    private static AuditRecorder Audit(AppDbContext context) => new(context, new TestHttpContextAccessor(), TimeProvider.System);

    private async Task WaitForDatabaseBlockAsync(string applicationName)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                    WHERE application_name = @name AND cardinality(pg_blocking_pids(pid)) > 0)
                """, connection);
            command.Parameters.AddWithValue("name", applicationName);
            if ((bool)(await command.ExecuteScalarAsync(timeout.Token))!) return;
            await Task.Delay(25, timeout.Token);
        }
    }

    private sealed class AuditBarrier : IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public void Dispose() => Release.Dispose();
    }

    private sealed class BlockingAudit(IAuditRecorder inner, AuditBarrier barrier) : IAuditRecorder
    {
        public void Record(string eventType, string result, int? userId = null, Guid? sessionId = null,
            string? entityType = null, string? entityId = null, IReadOnlyDictionary<string, object?>? details = null)
        {
            inner.Record(eventType, result, userId, sessionId, entityType, entityId, details);
            barrier.Entered.TrySetResult();
            if (!barrier.Release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Audit barrier timed out");
        }
    }

    private sealed class FailingAudit : IAuditRecorder
    {
        public void Record(string eventType, string result, int? userId = null, Guid? sessionId = null,
            string? entityType = null, string? entityId = null, IReadOnlyDictionary<string, object?>? details = null) =>
            throw new InvalidOperationException("Injected audit failure");
    }
}
