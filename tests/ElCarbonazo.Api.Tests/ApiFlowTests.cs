using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class ApiFlowTests : IAsyncLifetime
{
    private readonly TemporaryPostgresDatabase _database = new();
    private TestApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _factory = new TestApiFactory(_database.ConnectionString, _database.LogDirectory);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task Caja_cancellation_result_survives_restart_without_repeating_mutation()
    {
        var id = Guid.NewGuid();
        var url = $"/api/caja/operaciones/{id}";
        var token = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var request = new { tipo = "AbrirTurno", version = 1, argumentos = new { fondoInicial = 0m, confirmado = true } };
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(url, request)).StatusCode);
        // Discard the response as if the client could not consume it, then restart the host.
        using (var response = await _client.PostAsJsonAsync(url + "/cancelacion", new { confirmaIntervencion = false }))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _client.Dispose();
        await _factory.DisposeAsync();
        _factory = new TestApiFactory(_database.ConnectionString, _database.LogDirectory);
        _client = _factory.CreateClient();
        token = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var result = await _client.GetFromJsonAsync<JsonElement>(url);
        Assert.Equal("Cancelada", result.GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync(url + "/ejecucion", new { })).StatusCode);
        await using var verify = _database.CreateContext();
        Assert.Equal(1, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "OperacionCajaCancelada"));
        Assert.Empty(await verify.TurnosCaja.ToListAsync());
    }

    [Fact]
    public async Task Caja_http_rejects_unauthorized_roles_and_invalid_or_oversized_payloads()
    {
        var url = $"/api/caja/operaciones/{Guid.NewGuid()}";
        var kitchen = await LoginAsync("cocina", "kitchen password");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kitchen);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(url)).StatusCode);
        var owner = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner);
        using var duplicate = new StringContent("{\"tipo\":\"AbrirTurno\",\"tipo\":\"AbrirTurno\",\"version\":1,\"argumentos\":{\"fondoInicial\":0,\"confirmado\":true}}", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsync(url, duplicate)).StatusCode);
        using var oversize = new StringContent(new string('x', 32769), System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await _client.PutAsync(url, oversize)).StatusCode);
        await using var context = _database.CreateContext();
        Assert.Empty(await context.OperacionesCaja.ToListAsync());
    }

    [Fact]
    public async Task Caja_operation_http_requires_session_replays_and_cancels_without_business_execution()
    {
        var id = Guid.NewGuid();
        var url = $"/api/caja/operaciones/{id}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(url)).StatusCode);
        var token = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var body = new { tipo = "AbrirTurno", version = 1, argumentos = new { fondoInicial = 0m, confirmado = true } };
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(url, body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(url, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PutAsJsonAsync(url,
            new { tipo = "AbrirTurno", version = 1, argumentos = new { fondoInicial = 1m, confirmado = true } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _client.PostAsJsonAsync(url + "/ejecucion", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync(url + "/cancelacion", new { confirmaIntervencion = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync(url + "/ejecucion", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(url)).StatusCode);
        await using var context = _database.CreateContext();
        Assert.Empty(await context.TurnosCaja.ToListAsync());
        Assert.Equal(CajaStates.OperacionCancelada, await context.OperacionesCaja.Where(x => x.IdOperacion == id).Select(x => x.Estado).SingleAsync());
    }

    [Fact]
    public async Task Auth_mesas_groups_and_audit_follow_the_http_contract()
    {
        var anonymousResponse = await _client.GetAsync("/api/mesas");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.StartsWith("application/problem+json", anonymousResponse.Content.Headers.ContentType!.ToString());
        using (var unauthorized = JsonDocument.Parse(await anonymousResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("AUTH_NO_AUTENTICADO", unauthorized.RootElement.GetProperty("code").GetString());
        }

        using (var invalidHeader = new HttpRequestMessage(HttpMethod.Get, "/api/mesas"))
        {
            invalidHeader.Headers.Add("X-Installation-Id", "not-a-uuid");
            var response = await _client.SendAsync(invalidHeader);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("VALIDATION_ERROR", problem.RootElement.GetProperty("code").GetString());
            Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("xInstallationId", out _));
        }

        var invalidLogin = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { nombre = "  UNKNOWN ", contrasena = "sensitive-password-value" });
        Assert.Equal(HttpStatusCode.Unauthorized, invalidLogin.StatusCode);
        using (var invalid = JsonDocument.Parse(await invalidLogin.Content.ReadAsStringAsync()))
        {
            Assert.Equal("AUTH_CREDENCIALES_INVALIDAS", invalid.RootElement.GetProperty("code").GetString());
        }

        var ownerToken = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var getTables = await _client.GetAsync("/api/mesas");
        getTables.EnsureSuccessStatusCode();
        var tables = await getTables.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(tables);
        Assert.Equal(15, tables.Length);
        Assert.All(tables, table => Assert.True(table.TryGetProperty("version", out _)));

        var firstTable = tables.Single(table => table.GetProperty("idMesa").GetInt32() == 1);
        var firstVersion = firstTable.GetProperty("version").GetUInt32();
        var change = await _client.PutAsJsonAsync("/api/mesas/1/estado", new
        {
            estado = MesaStates.Ocupada,
            version = firstVersion
        });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        var updated = await change.Content.ReadFromJsonAsync<JsonElement>();
        var currentVersion = updated.GetProperty("version").GetUInt32();
        Assert.NotEqual(firstVersion, currentVersion);

        var sameState = await _client.PutAsJsonAsync("/api/mesas/1/estado", new
        {
            estado = MesaStates.Ocupada,
            version = currentVersion
        });
        Assert.Equal(HttpStatusCode.OK, sameState.StatusCode);
        Assert.Equal(currentVersion, (await sameState.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetUInt32());

        var unsupportedState = await _client.PutAsJsonAsync("/api/mesas/1/estado", new
        {
            estado = "Unknown",
            version = currentVersion
        });
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedState.StatusCode);

        var tableFive = tables.Single(table => table.GetProperty("idMesa").GetInt32() == 5);
        var reserved = await _client.PutAsJsonAsync("/api/mesas/5/estado", new
        {
            estado = MesaStates.Reservada,
            version = tableFive.GetProperty("version").GetUInt32()
        });
        Assert.Equal(HttpStatusCode.OK, reserved.StatusCode);
        var occupied = await _client.PutAsJsonAsync("/api/mesas/5/estado", new
        {
            estado = MesaStates.Ocupada,
            version = (await reserved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetUInt32()
        });
        Assert.Equal(HttpStatusCode.OK, occupied.StatusCode);
        var occupiedTable = await occupied.Content.ReadFromJsonAsync<JsonElement>();
        var disallowedTransition = await _client.PutAsJsonAsync("/api/mesas/5/estado", new
        {
            estado = MesaStates.Reservada,
            version = occupiedTable.GetProperty("version").GetUInt32()
        });
        Assert.Equal(HttpStatusCode.BadRequest, disallowedTransition.StatusCode);
        var freeAgain = await _client.PutAsJsonAsync("/api/mesas/5/estado", new
        {
            estado = MesaStates.Libre,
            version = occupiedTable.GetProperty("version").GetUInt32()
        });
        Assert.Equal(HttpStatusCode.OK, freeAgain.StatusCode);

        await using (var context = _database.CreateContext())
        {
            context.Pedidos.Add(new Pedido
            {
                IdMesa = 5,
                IdUsuario = 1,
                FechaHora = DateTimeOffset.UtcNow,
                Subtotal = 100m,
                Impuestos = 0m,
                Total = 100m,
                Estado = PedidoStates.Abierto
            });
            await context.SaveChangesAsync();
        }

        var occupiedAgain = await _client.PutAsJsonAsync("/api/mesas/5/estado", new
        {
            estado = MesaStates.Ocupada,
            version = (await freeAgain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetUInt32()
        });
        Assert.Equal(HttpStatusCode.OK, occupiedAgain.StatusCode);
        var blockedRelease = await _client.PutAsJsonAsync("/api/mesas/5/estado", new
        {
            estado = MesaStates.Libre,
            version = (await occupiedAgain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetUInt32()
        });
        Assert.Equal(HttpStatusCode.Conflict, blockedRelease.StatusCode);

        var staleNoOp = await _client.PutAsJsonAsync("/api/mesas/1/estado", new
        {
            estado = MesaStates.Ocupada,
            version = firstVersion
        });
        Assert.Equal(HttpStatusCode.Conflict, staleNoOp.StatusCode);
        using (var conflict = JsonDocument.Parse(await staleNoOp.Content.ReadAsStringAsync()))
        {
            Assert.Equal("MESA_CONFLICTO_CONCURRENCIA", conflict.RootElement.GetProperty("code").GetString());
        }

        var invalidGroup = await _client.PostAsJsonAsync("/api/grupos-mesa", new { mesas = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, invalidGroup.StatusCode);
        using (var validation = JsonDocument.Parse(await invalidGroup.Content.ReadAsStringAsync()))
        {
            Assert.Equal("VALIDATION_ERROR", validation.RootElement.GetProperty("code").GetString());
        }

        var groupTables = tables.Where(table =>
                table.GetProperty("idMesa").GetInt32() is 2 or 3)
            .Select(table => new
            {
                idMesa = table.GetProperty("idMesa").GetInt32(),
                version = table.GetProperty("version").GetUInt32()
            })
            .ToArray();
        var createGroup = await _client.PostAsJsonAsync("/api/grupos-mesa", new { mesas = groupTables });
        Assert.Equal(HttpStatusCode.Created, createGroup.StatusCode);
        var group = await createGroup.Content.ReadFromJsonAsync<JsonElement>();
        var groupId = group.GetProperty("idGrupo").GetInt32();
        Assert.Equal(2, group.GetProperty("mesas").GetArrayLength());
        Assert.All(group.GetProperty("mesas").EnumerateArray(), table =>
            Assert.Equal(MesaStates.Agrupada, table.GetProperty("estado").GetString()));

        await using (var context = _database.CreateContext())
        {
            context.Pedidos.Add(new Pedido
            {
                IdGrupo = groupId,
                IdUsuario = 1,
                FechaHora = DateTimeOffset.UtcNow,
                Subtotal = 100m,
                Impuestos = 0m,
                Total = 100m,
                Estado = PedidoStates.Listo
            });
            await context.SaveChangesAsync();
        }

        var blockedClose = await _client.DeleteAsync($"/api/grupos-mesa/{groupId}");
        Assert.Equal(HttpStatusCode.Conflict, blockedClose.StatusCode);
        using (var pendingProblem = JsonDocument.Parse(await blockedClose.Content.ReadAsStringAsync()))
        {
            Assert.Equal("GRUPO_CON_PEDIDOS_PENDIENTES", pendingProblem.RootElement.GetProperty("code").GetString());
        }

        await using (var context = _database.CreateContext())
        {
            await context.Pedidos
                .Where(order => order.IdGrupo == groupId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.Estado, PedidoStates.Pagado));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/grupos-mesa/{groupId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/grupos-mesa/{groupId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/grupos-mesa/2147483647")).StatusCode);

        await using (var context = _database.CreateContext())
        {
            Assert.Equal(2, await context.GruposMesaMiembros.CountAsync(member => member.IdGrupo == groupId));
            var closedGroup = await context.GruposMesa.SingleAsync(item => item.IdGrupo == groupId);
            Assert.False(closedGroup.Activo);
            Assert.Equal(1, closedGroup.IdUsuarioCierre);
            Assert.NotNull(closedGroup.FechaCierre);
        }

        var activityCount = await CountAuditEventsAsync();
        var activity = await _client.PostAsync("/api/auth/activity", content: null);
        Assert.Equal(HttpStatusCode.OK, activity.StatusCode);
        Assert.Equal(activityCount, await CountAuditEventsAsync());

        var logout = await _client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/mesas")).StatusCode);

        await AssertKitchenCannotMutateTablesAsync();
        await AssertFailedLoginsAndLockoutAsync();
        await AssertRateLimitHasOneAuditEventAsync();
        await AssertAuditRowsContainNoPasswordAsync();
        await AssertDatabaseConstraintsAreEnforcedAsync();
    }

    private async Task AssertFailedLoginsAndLockoutAsync()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var blankPassword = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { nombre = "jefa central", contrasena = " " });
        Assert.Equal(HttpStatusCode.Unauthorized, blankPassword.StatusCode);

        for (var attempt = 0; attempt < LoginSecurityPolicy.MaximumFailedAttempts; attempt++)
        {
            var response = await _client.PostAsJsonAsync(
                "/api/auth/login",
                new { nombre = "jefa central", contrasena = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        await using var context = _database.CreateContext();
        var user = await context.Usuarios.SingleAsync(item => item.IdUsuario == 1);
        Assert.Equal(LoginSecurityPolicy.MaximumFailedAttempts, user.IntentosFallidos);
        Assert.True(user.BloqueoHasta > DateTimeOffset.UtcNow);
        Assert.Equal(1, await context.EventosAuditoria.CountAsync(item => item.TipoEvento == "BloqueoUsuarioActivado"));
    }

    [Fact]
    public async Task Refresh_rotates_tokens_and_reuse_revokes_the_session()
    {
        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { nombre = "jefa central", contrasena = "correct horse battery staple" });
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var originalRefreshToken = login.GetProperty("refreshToken").GetString()!;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/auth/activity", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/auth/activity", content: null)).StatusCode);
        _client.DefaultRequestHeaders.Authorization = null;

        var refreshResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = originalRefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<JsonElement>();
        var rotatedRefreshToken = refreshed.GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(originalRefreshToken, rotatedRefreshToken);

        var replayResponse = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = originalRefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
        using (var replayProblem = JsonDocument.Parse(await replayResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("AUTH_REFRESH_INVALIDO", replayProblem.RootElement.GetProperty("code").GetString());
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            refreshed.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/mesas")).StatusCode);

        var invalid = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "" });
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);

        var secondLoginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { nombre = "jefa central", contrasena = "correct horse battery staple" });
        secondLoginResponse.EnsureSuccessStatusCode();
        var secondLogin = await secondLoginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondSessionId = Guid.Parse(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ReadJwtToken(secondLogin.GetProperty("accessToken").GetString())
            .Claims.Single(claim => claim.Type == "sid").Value);
        await using (var sessionContext = _database.CreateContext())
        {
            await sessionContext.SesionesUsuario
                .Where(session => session.IdSesion == secondSessionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(session => session.UltimaActividad, DateTimeOffset.UtcNow.AddMinutes(-20)));
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            secondLogin.GetProperty("accessToken").GetString());
        var expiredActivity = await _client.PostAsync("/api/auth/activity", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, expiredActivity.StatusCode);
        _client.DefaultRequestHeaders.Authorization = null;
        var expiredRefresh = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = secondLogin.GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.Unauthorized, expiredRefresh.StatusCode);

        await using var context = _database.CreateContext();
        Assert.Contains(await context.EventosAuditoria.Select(item => item.TipoEvento).ToListAsync(),
            eventType => eventType == "RefreshExitoso");
        Assert.Contains(await context.EventosAuditoria.Select(item => item.TipoEvento).ToListAsync(),
            eventType => eventType == "RefreshReutilizado");
        Assert.Contains(await context.EventosAuditoria.Select(item => item.TipoEvento).ToListAsync(),
            eventType => eventType == "SesionRevocadaPorReusoRefresh");
    }

    [Fact]
    public async Task Product_catalog_is_authorized_filtered_and_cursor_paged()
    {
        var anonymous = await _client.GetAsync("/api/productos");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await using (var context = _database.CreateContext())
        {
            context.Productos.AddRange(Enumerable.Range(1, 30).Select(index => new Producto
            {
                Nombre = $"Producto de prueba {index:D2}",
                Categoria = index % 2 == 0 ? "Comida" : "Bebida",
                Precio = index,
                Disponible = true
            }));
            context.Productos.Add(new Producto
            {
                Nombre = "Producto no disponible",
                Categoria = "Licor",
                Precio = 50m,
                Disponible = false
            });
            await context.SaveChangesAsync();
        }

        var ownerToken = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var firstResponse = await _client.GetAsync("/api/productos?limite=25");
        firstResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var firstItems = first.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(25, firstItems.Length);
        Assert.All(firstItems, item => Assert.True(item.GetProperty("disponible").GetBoolean()));
        Assert.All(firstItems, item => Assert.True(item.TryGetProperty("version", out _)));
        var cursor = first.GetProperty("cursorSiguiente").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));

        var secondResponse = await _client.GetAsync($"/api/productos?limite=25&cursor={Uri.EscapeDataString(cursor!)}");
        secondResponse.EnsureSuccessStatusCode();
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        var secondItems = second.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(5, secondItems.Length);
        Assert.Null(second.GetProperty("cursorSiguiente").GetString());
        Assert.Empty(firstItems.Select(item => item.GetProperty("idProducto").GetInt32())
            .Intersect(secondItems.Select(item => item.GetProperty("idProducto").GetInt32())));

        var mismatchedCursor = await _client.GetAsync(
            $"/api/productos?disponibles=false&cursor={Uri.EscapeDataString(cursor!)}");
        Assert.Equal(HttpStatusCode.BadRequest, mismatchedCursor.StatusCode);
        using (var problem = JsonDocument.Parse(await mismatchedCursor.Content.ReadAsStringAsync()))
        {
            Assert.Equal("VALIDATION_ERROR", problem.RootElement.GetProperty("code").GetString());
        }

        var invalidLimit = await _client.GetAsync("/api/productos?limite=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidLimit.StatusCode);

        _client.DefaultRequestHeaders.Authorization = null;
        var kitchenToken = await LoginAsync("cocina", "kitchen password");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kitchenToken);
        var kitchenResponse = await _client.GetAsync("/api/productos");
        Assert.Equal(HttpStatusCode.Forbidden, kitchenResponse.StatusCode);
    }

    [Fact]
    public async Task Active_account_returns_empty_legacy_and_enforces_permissions()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/pedidos/activo?mesaId=1")).StatusCode);
        var token = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var empty = await _client.GetFromJsonAsync<JsonElement>("/api/pedidos/activo?mesaId=1");
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("pedido").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/pedidos/activo?mesaId=1&grupoId=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/pedidos/activo?mesaId=99")).StatusCode);

        await using (var context = _database.CreateContext())
        {
            context.Pedidos.Add(new Pedido
            {
                IdMesa = 1,
                IdUsuario = 1,
                FechaHora = DateTimeOffset.UtcNow,
                Subtotal = 42m,
                Impuestos = 0m,
                Total = 42m,
                Estado = PedidoStates.Listo
            });
            await context.SaveChangesAsync();
        }

        var legacy = await _client.GetFromJsonAsync<JsonElement>("/api/pedidos/activo?mesaId=1");
        var pedido = legacy.GetProperty("pedido");
        Assert.True(pedido.GetProperty("soloLectura").GetBoolean());
        Assert.Empty(pedido.GetProperty("comandas").EnumerateArray());
        Assert.Equal(42m, pedido.GetProperty("total").GetDecimal());

        _client.DefaultRequestHeaders.Authorization = null;
        var kitchen = await LoginAsync("cocina", "kitchen password");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kitchen);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/pedidos/activo?mesaId=1")).StatusCode);
    }

    [Fact]
    public async Task Order_send_is_atomic_idempotent_and_preserves_lines()
    {
        int productId;
        await using (var context = _database.CreateContext())
        {
            var product = new Producto { Nombre = "Prueba", Categoria = "Comida", Precio = 10.10m, Disponible = true };
            context.Productos.Add(product);
            await context.SaveChangesAsync();
            productId = product.IdProducto;
        }

        var token = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var mesa = (await _client.GetFromJsonAsync<JsonElement[]>("/api/mesas"))!.Single(item => item.GetProperty("idMesa").GetInt32() == 1);
        var envioId = Guid.NewGuid();
        var body = new
        {
            envioId,
            mesaId = 1,
            versionMesa = mesa.GetProperty("version").GetUInt32(),
            lineas = new[]
            {
                new { idProducto = productId, versionProducto = 1, cantidad = 2, notas = "Cafe\u0301" },
                new { idProducto = productId, versionProducto = 1, cantidad = 1, notas = "" }
            }
        };
        var first = await _client.PostAsJsonAsync("/api/pedidos/envios", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstJson = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(30.30m, firstJson.GetProperty("pedido").GetProperty("total").GetDecimal());
        var replay = await _client.PostAsJsonAsync("/api/pedidos/envios", body);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(firstJson.GetProperty("idComanda").GetInt32(),
            (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("idComanda").GetInt32());
        Assert.Equal(first.Headers.Location, replay.Headers.Location);

        var normalizedReplay = await _client.PostAsJsonAsync("/api/pedidos/envios", new
        {
            envioId,
            mesaId = 1,
            versionMesa = mesa.GetProperty("version").GetUInt32(),
            lineas = new[]
            {
                new { idProducto = productId, versionProducto = 1, cantidad = 2, notas = "Café" },
                new { idProducto = productId, versionProducto = 1, cantidad = 1, notas = "" }
            }
        });
        Assert.Equal(HttpStatusCode.Created, normalizedReplay.StatusCode);

        var conflict = await _client.PostAsJsonAsync("/api/pedidos/envios", new
        {
            envioId,
            mesaId = 1,
            versionMesa = mesa.GetProperty("version").GetUInt32(),
            lineas = new[] { new { idProducto = productId, versionProducto = 1, cantidad = 1, notas = "" } }
        });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("ENVIO_ID_REUTILIZADO", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await using var verify = _database.CreateContext();
        Assert.Equal(1, await verify.Pedidos.CountAsync());
        Assert.Equal(1, await verify.Comandas.CountAsync());
        Assert.Equal(2, await verify.DetallesPedido.CountAsync());
        Assert.Contains(await verify.DetallesPedido.Select(item => item.Notas).ToListAsync(), note => note == "Café");
        Assert.Equal(1, await verify.EventosAuditoria.CountAsync(item => item.TipoEvento == "ComandaEnviada"));
        Assert.Equal("Ocupada", await verify.Mesas.Where(item => item.IdMesa == 1).Select(item => item.Estado).SingleAsync());
    }

    [Fact]
    public async Task Concurrent_identical_sends_produce_one_command_and_catalog_conflict_rolls_back()
    {
        int productId;
        await using (var context = _database.CreateContext())
        {
            var product = new Producto { Nombre = "Prueba", Categoria = "Bebida", Precio = 5m, Disponible = true };
            context.Productos.Add(product);
            await context.SaveChangesAsync();
            productId = product.IdProducto;
        }
        var token = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var mesas = (await _client.GetFromJsonAsync<JsonElement[]>("/api/mesas"))!;
        var version = mesas.Single(item => item.GetProperty("idMesa").GetInt32() == 1).GetProperty("version").GetUInt32();
        var body = new
        {
            envioId = Guid.NewGuid(),
            mesaId = 1,
            versionMesa = version,
            lineas = new[] { new { idProducto = productId, versionProducto = 1, cantidad = 1 } }
        };
        var responses = await Task.WhenAll(
            _client.PostAsJsonAsync("/api/pedidos/envios", body),
            _client.PostAsJsonAsync("/api/pedidos/envios", body));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var left = await responses[0].Content.ReadFromJsonAsync<JsonElement>();
        var right = await responses[1].Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(left.GetProperty("idComanda").GetInt32(), right.GetProperty("idComanda").GetInt32());

        var secondVersion = mesas.Single(item => item.GetProperty("idMesa").GetInt32() == 2).GetProperty("version").GetUInt32();
        var invalid = await _client.PostAsJsonAsync("/api/pedidos/envios", new
        {
            envioId = Guid.NewGuid(),
            mesaId = 2,
            versionMesa = secondVersion,
            lineas = new[] { new { idProducto = productId, versionProducto = 999, cantidad = 1 } }
        });
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        Assert.Equal("CATALOGO_CAMBIADO", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await using var verify = _database.CreateContext();
        Assert.Equal(1, await verify.Comandas.CountAsync());
        Assert.Equal(1, await verify.EnviosPedido.CountAsync());
        Assert.Equal("Libre", await verify.Mesas.Where(item => item.IdMesa == 2).Select(item => item.Estado).SingleAsync());
    }

    [Fact]
    public async Task Group_sends_share_account_and_block_closing_group()
    {
        int productId;
        await using (var context = _database.CreateContext())
        {
            var product = new Producto { Nombre = "Prueba", Categoria = "Comida", Precio = 8m, Disponible = true };
            context.Productos.Add(product);
            await context.SaveChangesAsync();
            productId = product.IdProducto;
        }
        var token = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var tables = (await _client.GetFromJsonAsync<JsonElement[]>("/api/mesas"))!;
        var members = tables.Where(item => item.GetProperty("idMesa").GetInt32() is 2 or 3)
            .Select(item => new { idMesa = item.GetProperty("idMesa").GetInt32(), version = item.GetProperty("version").GetUInt32() }).ToArray();
        var groupResponse = await _client.PostAsJsonAsync("/api/grupos-mesa", new { mesas = members });
        Assert.Equal(HttpStatusCode.Created, groupResponse.StatusCode);
        var group = (await groupResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("idGrupo").GetInt32();
        for (var i = 0; i < 2; i++)
        {
            var response = await _client.PostAsJsonAsync("/api/pedidos/envios", new
            {
                envioId = Guid.NewGuid(),
                grupoId = group,
                lineas = new[] { new { idProducto = productId, versionProducto = 1, cantidad = 1 } }
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        var account = await _client.GetFromJsonAsync<JsonElement>($"/api/pedidos/activo?grupoId={group}");
        Assert.Equal(2, account.GetProperty("pedido").GetProperty("comandas").GetArrayLength());
        Assert.Equal(16m, account.GetProperty("pedido").GetProperty("total").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync($"/api/grupos-mesa/{group}")).StatusCode);
        await using var verify = _database.CreateContext();
        Assert.Equal(1, await verify.Pedidos.CountAsync());
        Assert.Equal(2, await verify.Mesas.CountAsync(item => item.IdGrupo == group && item.Estado == "Agrupada"));
    }

    [Fact]
    public async Task Order_send_rejects_invalid_bodies_limits_legacy_and_unauthorized_role()
    {
        var token = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var oversized = await _client.PostAsync("/api/pedidos/envios",
            new StringContent(new string('x', 65537), System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal("REQUEST_TOO_LARGE", (await oversized.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var badCount = await _client.PostAsJsonAsync("/api/pedidos/envios", new
        {
            envioId = Guid.NewGuid(),
            mesaId = 1,
            versionMesa = 1,
            lineas = Enumerable.Range(0, 51).Select(_ => new { idProducto = 1, versionProducto = 1, cantidad = 1 }).ToArray()
        });
        Assert.Equal(HttpStatusCode.BadRequest, badCount.StatusCode);
        await using (var context = _database.CreateContext())
        {
            context.Pedidos.Add(new Pedido
            {
                IdMesa = 1,
                IdUsuario = 1,
                FechaHora = DateTimeOffset.UtcNow,
                Subtotal = 10m,
                Impuestos = 0m,
                Total = 10m,
                Estado = PedidoStates.Listo
            });
            await context.SaveChangesAsync();
        }
        var mesas = (await _client.GetFromJsonAsync<JsonElement[]>("/api/mesas"))!;
        var version = mesas.Single(item => item.GetProperty("idMesa").GetInt32() == 1).GetProperty("version").GetUInt32();
        var body = new
        {
            envioId = Guid.NewGuid(),
            mesaId = 1,
            versionMesa = version,
            lineas = new[] { new { idProducto = 1, versionProducto = 1, cantidad = 1 } }
        };
        var legacy = await _client.PostAsJsonAsync("/api/pedidos/envios", body);
        Assert.Equal(HttpStatusCode.Conflict, legacy.StatusCode);
        Assert.Equal("CUENTA_LEGADA_SIN_COMANDAS", (await legacy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        _client.DefaultRequestHeaders.Authorization = null;
        var kitchen = await LoginAsync("cocina", "kitchen password");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kitchen);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsJsonAsync("/api/pedidos/envios", body)).StatusCode);
        await using var verify = _database.CreateContext();
        Assert.Empty(await verify.Comandas.ToListAsync());
        Assert.Empty(await verify.EnviosPedido.ToListAsync());
    }

    [Fact]
    public async Task Lost_response_can_be_replayed_after_api_restart_with_same_intention()
    {
        int productId;
        await using (var context = _database.CreateContext())
        {
            var product = new Producto { Nombre = "Prueba", Categoria = "Bebida", Precio = 3m, Disponible = true };
            context.Productos.Add(product);
            await context.SaveChangesAsync();
            productId = product.IdProducto;
        }
        var token = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var tables = (await _client.GetFromJsonAsync<JsonElement[]>("/api/mesas"))!;
        var version = tables.Single(x => x.GetProperty("idMesa").GetInt32() == 1).GetProperty("version").GetUInt32();
        var body = new
        {
            envioId = Guid.NewGuid(),
            mesaId = 1,
            versionMesa = version,
            lineas = new[] { new { idProducto = productId, versionProducto = 1, cantidad = 1, notas = "Café" } }
        };
        var first = await _client.PostAsJsonAsync("/api/pedidos/envios", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var expected = await first.Content.ReadFromJsonAsync<JsonElement>();
        var location = first.Headers.Location;
        _client.Dispose();
        await _factory.DisposeAsync();
        _factory = new TestApiFactory(_database.ConnectionString, _database.LogDirectory);
        _client = _factory.CreateClient();
        var newToken = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
        var replay = await _client.PostAsJsonAsync("/api/pedidos/envios", body);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(location, replay.Headers.Location);
        Assert.True(JsonElement.DeepEquals(expected, await replay.Content.ReadFromJsonAsync<JsonElement>()));
        await using (var updateUser = _database.CreateContext())
        {
            await updateUser.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Mesero' WHERE id_usuario = 2");
        }
        _client.DefaultRequestHeaders.Authorization = null;
        var otherToken = await LoginAsync("cocina", "kitchen password");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        var differentActor = await _client.PostAsJsonAsync("/api/pedidos/envios", body);
        Assert.Equal(HttpStatusCode.Conflict, differentActor.StatusCode);
        Assert.Equal("ENVIO_ID_REUTILIZADO", (await differentActor.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await using var verify = _database.CreateContext();
        Assert.Equal(1, await verify.Comandas.CountAsync());
        Assert.Equal(1, await verify.EnviosPedido.CountAsync());
    }

    [Fact]
    public async Task Replay_rechecks_current_role_and_rejects_revoked_authorization()
    {
        int productId;
        await using (var context = _database.CreateContext())
        {
            var product = new Producto { Nombre = "Prueba", Categoria = "Bebida", Precio = 3m, Disponible = true };
            context.Productos.Add(product);
            await context.SaveChangesAsync();
            productId = product.IdProducto;
        }
        var token = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var tables = (await _client.GetFromJsonAsync<JsonElement[]>("/api/mesas"))!;
        var body = new
        {
            envioId = Guid.NewGuid(),
            mesaId = 1,
            versionMesa = tables.Single(x => x.GetProperty("idMesa").GetInt32() == 1).GetProperty("version").GetUInt32(),
            lineas = new[] { new { idProducto = productId, versionProducto = 1, cantidad = 1 } }
        };
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/pedidos/envios", body)).StatusCode);
        await using (var context = _database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Personal de Cocina' WHERE id_usuario = 1");
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/pedidos/envios", body)).StatusCode);
        await using var verify = _database.CreateContext();
        Assert.Equal(1, await verify.Comandas.CountAsync());
    }

    [Fact]
    public async Task Timeout_after_commit_can_be_replayed_without_duplicate_command()
    {
        int productId;
        await using (var context = _database.CreateContext())
        {
            var product = new Producto { Nombre = "Prueba", Categoria = "Comida", Precio = 10m, Disponible = true };
            context.Productos.Add(product);
            await context.SaveChangesAsync();
            productId = product.IdProducto;
        }
        var gate = new PostCommitGate();
        await using var factory = new TestApiFactory(_database.ConnectionString, _database.LogDirectory, gate);
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { nombre = "jefa central", contrasena = "correct horse battery staple" });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var tables = (await client.GetFromJsonAsync<JsonElement[]>("/api/mesas"))!;
        var version = tables.Single(x => x.GetProperty("idMesa").GetInt32() == 1).GetProperty("version").GetUInt32();
        var body = new
        {
            envioId = Guid.NewGuid(),
            mesaId = 1,
            versionMesa = version,
            lineas = new[] { new { idProducto = productId, versionProducto = 1, cantidad = 1 } }
        };
        using var timeout = new CancellationTokenSource();
        var first = client.PostAsJsonAsync("/api/pedidos/envios", body, timeout.Token);
        await gate.Committed.Task.WaitAsync(TimeSpan.FromSeconds(20));
        timeout.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var retry = await client.PostAsJsonAsync("/api/pedidos/envios", body);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        await using var verify = _database.CreateContext();
        Assert.Equal(1, await verify.Comandas.CountAsync());
        Assert.Equal(1, await verify.EnviosPedido.CountAsync());
    }

    [Fact]
    public async Task Send_body_64k_boundary_chunked_limit_and_decimal_quantity_are_validated()
    {
        int productId;
        await using (var context = _database.CreateContext())
        {
            var product = new Producto { Nombre = "Prueba", Categoria = "Bebida", Precio = 1m, Disponible = true };
            context.Productos.Add(product);
            await context.SaveChangesAsync();
            productId = product.IdProducto;
        }
        var token = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var tables = (await _client.GetFromJsonAsync<JsonElement[]>("/api/mesas"))!;
        var version = tables.Single(x => x.GetProperty("idMesa").GetInt32() == 1).GetProperty("version").GetUInt32();
        var json = JsonSerializer.Serialize(new
        {
            envioId = Guid.NewGuid(),
            mesaId = 1,
            versionMesa = version,
            lineas = new[] { new { idProducto = productId, versionProducto = 1, cantidad = 1 } }
        });
        var exact = json.PadRight(65536, ' ');
        using var exactRequest = new HttpRequestMessage(HttpMethod.Post, "/api/pedidos/envios")
        {
            Content = new StringContent(exact, System.Text.Encoding.UTF8, "application/json")
        };
        exactRequest.Headers.TransferEncodingChunked = true;
        Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(exactRequest)).StatusCode);
        using var oversized = new HttpRequestMessage(HttpMethod.Post, "/api/pedidos/envios")
        {
            Content = new UnknownLengthContent(exact + " ")
        };
        oversized.Headers.TransferEncodingChunked = true;
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await _client.SendAsync(oversized)).StatusCode);
        var decimalQuantity = await _client.PostAsJsonAsync("/api/pedidos/envios", new
        {
            envioId = Guid.NewGuid(),
            mesaId = 2,
            versionMesa = tables.Single(x => x.GetProperty("idMesa").GetInt32() == 2).GetProperty("version").GetUInt32(),
            lineas = new[] { new { idProducto = productId, versionProducto = 1, cantidad = 1.5m } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, decimalQuantity.StatusCode);
        await using var verify = _database.CreateContext();
        Assert.Equal(1, await verify.Comandas.CountAsync());
    }

    [Fact]
    public async Task Kitchen_queue_and_transitions_preserve_account_and_enforce_roles()
    {
        int productId;
        await using (var context = _database.CreateContext())
        {
            var product = new Producto { Nombre = "Prueba cocina", Categoria = "Comida", Precio = 5m, Disponible = true };
            context.Productos.Add(product);
            await context.SaveChangesAsync();
            productId = product.IdProducto;
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/cocina/cola")).StatusCode);
        var owner = await LoginAsync("jefa central", "correct horse battery staple");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner);
        var tables = (await _client.GetFromJsonAsync<JsonElement[]>("/api/mesas"))!;
        var mesaVersion = tables.Single(item => item.GetProperty("idMesa").GetInt32() == 1).GetProperty("version").GetUInt32();
        var first = await _client.PostAsJsonAsync("/api/pedidos/envios", new
        {
            envioId = Guid.NewGuid(),
            mesaId = 1,
            versionMesa = mesaVersion,
            lineas = new[] { new { idProducto = productId, versionProducto = 1, cantidad = 2, notas = "Sin sal" } }
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var payload = await first.Content.ReadFromJsonAsync<JsonElement>();
        var id = payload.GetProperty("idComanda").GetInt32();
        mesaVersion = payload.GetProperty("versionMesa").GetUInt32();
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/pedidos/envios", new
        {
            envioId = Guid.NewGuid(),
            mesaId = 1,
            versionMesa = mesaVersion,
            lineas = new[] { new { idProducto = productId, versionProducto = 1, cantidad = 1, notas = "" } }
        })).StatusCode);

        _client.DefaultRequestHeaders.Authorization = null;
        var kitchen = await LoginAsync("cocina", "kitchen password");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kitchen);
        var queue = await _client.GetFromJsonAsync<JsonElement>("/api/cocina/cola?limite=1");
        var item = queue.GetProperty("items")[0];
        Assert.Equal(id, item.GetProperty("idComanda").GetInt32());
        Assert.Equal("Sin sal", item.GetProperty("lineas")[0].GetProperty("notas").GetString());
        var cursor = queue.GetProperty("cursorSiguiente").GetString();
        Assert.NotNull(cursor);
        var page2 = await _client.GetFromJsonAsync<JsonElement>($"/api/cocina/cola?limite=1&cursor={Uri.EscapeDataString(cursor)}");
        Assert.NotEqual(id, page2.GetProperty("items")[0].GetProperty("idComanda").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/cocina/cola?cursor=invalid!")).StatusCode);
        var version = item.GetProperty("version").GetUInt32();
        var skip = await _client.PutAsJsonAsync($"/api/cocina/comandas/{id}/estado", new { estado = "Lista", versionComanda = version });
        Assert.Equal(HttpStatusCode.Conflict, skip.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync($"/api/cocina/comandas/{id}/estado",
            new { estado = "Cancelada", versionComanda = version })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync($"/api/cocina/comandas/{id}/estado",
            new { estado = "En Preparación" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync("/api/cocina/comandas/2147483647/estado",
            new { estado = "En Preparación", versionComanda = version })).StatusCode);
        var prepare = await _client.PutAsJsonAsync($"/api/cocina/comandas/{id}/estado", new { estado = "En Preparación", versionComanda = version });
        Assert.Equal(HttpStatusCode.OK, prepare.StatusCode);
        var prepared = await prepare.Content.ReadFromJsonAsync<JsonElement>();
        var stale = await _client.PutAsJsonAsync($"/api/cocina/comandas/{id}/estado", new { estado = "Lista", versionComanda = version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("VERSION_CONFLICT", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var ready = await _client.PutAsJsonAsync($"/api/cocina/comandas/{id}/estado", new
        {
            estado = "Lista",
            versionComanda = prepared.GetProperty("version").GetUInt32()
        });
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var remaining = await _client.GetFromJsonAsync<JsonElement>("/api/cocina/cola");
        Assert.Single(remaining.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/pedidos/activo?mesaId=1")).StatusCode);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner);
        var account = (await _client.GetFromJsonAsync<JsonElement>("/api/pedidos/activo?mesaId=1")).GetProperty("pedido");
        Assert.Equal("Abierto", account.GetProperty("estado").GetString());
        Assert.Contains(account.GetProperty("comandas").EnumerateArray(), command => command.GetProperty("estado").GetString() == "Lista");
        var second = remaining.GetProperty("items")[0];
        var secondId = second.GetProperty("idComanda").GetInt32();
        var secondPrepare = await _client.PutAsJsonAsync($"/api/cocina/comandas/{secondId}/estado", new
        {
            estado = "En Preparación",
            versionComanda = second.GetProperty("version").GetUInt32()
        });
        Assert.Equal(HttpStatusCode.OK, secondPrepare.StatusCode);
        var secondPrepared = await secondPrepare.Content.ReadFromJsonAsync<JsonElement>();
        var secondReady = await _client.PutAsJsonAsync($"/api/cocina/comandas/{secondId}/estado", new
        {
            estado = "Lista",
            versionComanda = secondPrepared.GetProperty("version").GetUInt32()
        });
        Assert.Equal(HttpStatusCode.OK, secondReady.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<JsonElement>("/api/cocina/cola")).GetProperty("items").EnumerateArray());
        var readyAccount = (await _client.GetFromJsonAsync<JsonElement>("/api/pedidos/activo?mesaId=1")).GetProperty("pedido");
        Assert.Equal("Listo", readyAccount.GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/pedidos/envios", new
        {
            envioId = Guid.NewGuid(),
            mesaId = 1,
            versionMesa = mesaVersion,
            lineas = new[] { new { idProducto = productId, versionProducto = 1, cantidad = 1, notas = "" } }
        })).StatusCode);
        var reopened = (await _client.GetFromJsonAsync<JsonElement>("/api/pedidos/activo?mesaId=1")).GetProperty("pedido");
        Assert.Equal("Abierto", reopened.GetProperty("estado").GetString());
        Assert.Equal(2, reopened.GetProperty("comandas").EnumerateArray().Count(command => command.GetProperty("estado").GetString() == "Lista"));
        await using var verify = _database.CreateContext();
        Assert.Equal(4, await verify.EventosAuditoria.CountAsync(evento => evento.TipoEvento == "ComandaEstadoCambiado"));
        var auditDetails = await verify.EventosAuditoria.Where(evento => evento.TipoEvento == "ComandaEstadoCambiado")
            .Select(evento => evento.Detalles).ToListAsync();
        Assert.DoesNotContain(auditDetails, item => item.Contains("Sin sal", StringComparison.Ordinal));
        Assert.DoesNotContain(auditDetails, item => item.Contains("Prueba cocina", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Mesero")]
    [InlineData("Encargado de Caja")]
    public async Task Salon_roles_cannot_access_kitchen_queue_or_transition(string role)
    {
        await using (var context = _database.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE usuario SET rol = {role} WHERE id_usuario = 2");
        }
        var token = await LoginAsync("cocina", "kitchen password");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/cocina/cola")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PutAsJsonAsync("/api/cocina/comandas/1/estado",
            new { estado = "Lista", versionComanda = 1 })).StatusCode);
    }

    [Fact]
    public async Task Supervisor_can_read_kitchen_and_legacy_account_creates_no_ticket()
    {
        int commandId;
        await using (var context = _database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE usuario SET rol = 'Supervisor' WHERE id_usuario = 2");
            context.Pedidos.Add(new Pedido
            {
                IdMesa = 1,
                IdUsuario = 1,
                FechaHora = DateTimeOffset.UtcNow,
                Subtotal = 10m,
                Impuestos = 0m,
                Total = 10m,
                Estado = PedidoStates.Listo
            });
            await context.SaveChangesAsync();
            var product = new Producto { Nombre = "Prueba", Categoria = "Comida", Precio = 1m, Disponible = true };
            context.Productos.Add(product);
            await context.SaveChangesAsync();
            var version = await context.Mesas.Where(x => x.IdMesa == 2).Select(x => x.Version).SingleAsync();
            var audit = new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System);
            var sent = await new ElCarbonazo.Api.Services.EnviosService(context, audit, TimeProvider.System).EnviarAsync(
                new ElCarbonazo.Api.DTOs.EnvioRequest(Guid.NewGuid(), 2, null, version,
                    [new ElCarbonazo.Api.DTOs.EnvioLineaRequest(product.IdProducto, product.Version, 1, "")]),
                1, CancellationToken.None);
            Assert.Equal(201, sent.Status);
            commandId = await context.Comandas.Select(x => x.IdComanda).SingleAsync();
        }
        var token = await LoginAsync("cocina", "kitchen password");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var queue = await _client.GetFromJsonAsync<JsonElement>("/api/cocina/cola");
        var ticket = Assert.Single(queue.GetProperty("items").EnumerateArray());
        Assert.Equal(commandId, ticket.GetProperty("idComanda").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/api/cocina/comandas/{commandId}/estado",
            new { estado = "En Preparación", versionComanda = ticket.GetProperty("version").GetUInt32() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync("/api/cocina/comandas/2147483647/estado",
            new { estado = "En Preparación", versionComanda = 1 })).StatusCode);
        await using var verify = _database.CreateContext();
        Assert.Equal("Listo", await verify.Pedidos.Where(x => x.IdMesa == 1).Select(x => x.Estado).SingleAsync());
        Assert.Equal(1, await verify.Comandas.CountAsync());
        Assert.Equal(1, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "ComandaEstadoCambiado"));
    }

    private async Task<string> LoginAsync(string name, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { nombre = name, contrasena = password });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.TryGetProperty("refreshToken", out _));
        Assert.True(payload.TryGetProperty("accessTokenExpiresAt", out _));
        return payload.GetProperty("accessToken").GetString()!;
    }

    private async Task AssertKitchenCannotMutateTablesAsync()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var kitchenToken = await LoginAsync("cocina", "kitchen password");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kitchenToken);
        var table = (await _client.GetFromJsonAsync<JsonElement[]>("/api/mesas"))!
            .Single(item => item.GetProperty("idMesa").GetInt32() == 4);
        var forbidden = await _client.PutAsJsonAsync("/api/mesas/4/estado", new
        {
            estado = MesaStates.Ocupada,
            version = table.GetProperty("version").GetUInt32()
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var problem = JsonDocument.Parse(await forbidden.Content.ReadAsStringAsync());
        Assert.Equal("AUTH_PROHIBIDO", problem.RootElement.GetProperty("code").GetString());
    }

    private async Task AssertRateLimitHasOneAuditEventAsync()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var rateLimited = false;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var response = await _client.PostAsJsonAsync(
                "/api/auth/login",
                new { nombre = "missing user", contrasena = "wrong password" });
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimited = true;
                Assert.NotNull(response.Headers.RetryAfter);
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal("AUTH_DEMASIADOS_INTENTOS", problem.RootElement.GetProperty("code").GetString());
            }
        }

        Assert.True(rateLimited, "The login limit should reject excess requests.");
        await using var context = _database.CreateContext();
        Assert.Equal(1, await context.EventosAuditoria.CountAsync(item => item.TipoEvento == "LoginRateLimitActivado"));
    }

    private async Task<int> CountAuditEventsAsync()
    {
        await using var context = _database.CreateContext();
        return await context.EventosAuditoria.CountAsync();
    }

    private async Task AssertAuditRowsContainNoPasswordAsync()
    {
        await using var context = _database.CreateContext();
        var events = await context.EventosAuditoria.AsNoTracking().ToListAsync();
        Assert.Contains(events, item => item.TipoEvento == "LoginExitoso");
        Assert.Contains(events, item => item.TipoEvento == "MesaEstadoCambiado");
        Assert.Contains(events, item => item.TipoEvento == "GrupoMesaCreado");
        Assert.Contains(events, item => item.TipoEvento == "GrupoMesaCerrado");
        Assert.Contains(events, item => item.TipoEvento == "Logout");
        Assert.Contains(events, item => item.TipoEvento == "LoginRateLimitActivado");
        Assert.Contains(events, item => item.Detalles.Contains("nombreIntentado", StringComparison.Ordinal));
        Assert.DoesNotContain(events, item => item.Detalles.Contains("sensitive-password-value", StringComparison.Ordinal));
        Assert.DoesNotContain(events, item => item.Detalles.Contains("correct horse battery staple", StringComparison.Ordinal));
        Assert.DoesNotContain(events, item => item.Detalles.Contains("kitchen password", StringComparison.Ordinal));
    }

    private async Task AssertDatabaseConstraintsAreEnforcedAsync()
    {
        await using var context = _database.CreateContext();
        foreach (var invalidWrite in new[]
                 {
                     "UPDATE mesa SET capacidad = 0 WHERE id_mesa = 6",
                     "UPDATE mesa SET estado = 'Invalid' WHERE id_mesa = 6",
                     "UPDATE mesa SET numero = 16 WHERE id_mesa = 6",
                     "UPDATE usuario SET rol = 'Invalid' WHERE id_usuario = 1"
                 })
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => context.Database.ExecuteSqlRawAsync(invalidWrite));
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        }

        var audit = await context.EventosAuditoria.FirstAsync();
        await Assert.ThrowsAsync<PostgresException>(async () =>
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE evento_auditoria SET resultado = 'Error' WHERE id_evento = {audit.IdEvento}"));
        await Assert.ThrowsAsync<PostgresException>(async () =>
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM evento_auditoria WHERE id_evento = {audit.IdEvento}"));
    }
}

internal sealed class UnknownLengthContent(string text) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(text)).AsTask();

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}

public sealed class MesaTransitionUnitTests
{
    [Theory]
    [InlineData(MesaStates.Libre, MesaStates.Reservada, true)]
    [InlineData(MesaStates.Libre, MesaStates.Ocupada, true)]
    [InlineData(MesaStates.Reservada, MesaStates.Libre, true)]
    [InlineData(MesaStates.Reservada, MesaStates.Ocupada, true)]
    [InlineData(MesaStates.Ocupada, MesaStates.Libre, true)]
    [InlineData(MesaStates.Ocupada, MesaStates.Reservada, false)]
    [InlineData(MesaStates.Agrupada, MesaStates.Libre, false)]
    public void Transition_matrix_matches_domain(string current, string requested, bool expected) =>
        Assert.Equal(expected, MesaStateTransitions.IsAllowed(current, requested));
}

internal sealed class TestApiFactory(string connectionString, string logDirectory, PostCommitGate? postCommitGate = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        builder.UseEnvironment(Environments.Development);
        if (postCommitGate is not null)
        {
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new PostCommitFilter(postCommitGate)));
        }
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Jwt:Key"] = jwtKey,
                ["Jwt:Issuer"] = "ElCarbonazo.Api.Tests",
                ["Jwt:Audience"] = "ElCarbonazo.Desktop.Tests",
                ["Jwt:ExpirationMinutes"] = "5",
                ["PasswordHashing:WorkFactor"] = "10",
                ["Logging:FileDirectory"] = logDirectory
            }));
    }
}

internal sealed class PostCommitGate
{
    public TaskCompletionSource Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Triggered;
}

internal sealed class PostCommitFilter(PostCommitGate gate) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            await continuation();
            if (context.Request.Path == "/api/pedidos/envios" && context.Response.StatusCode == 201 &&
                Interlocked.Exchange(ref gate.Triggered, 1) == 0)
            {
                gate.Committed.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
            }
        });
        next(app);
    };
}

internal sealed class TemporaryPostgresDatabase : IAsyncLifetime
{
    private readonly string _databaseName = $"elcarbonazo_test_{Guid.NewGuid():N}";
    private string _adminConnectionString = null!;

    public string ConnectionString { get; private set; } = null!;
    public string LogDirectory { get; } = Path.Combine(Path.GetTempPath(), $"elcarbonazo-api-tests-{Guid.NewGuid():N}");

    public Task InitializeAsync() => InitializeAsync(null);

    public async Task InitializeAsync(string? migrationTarget)
    {
        var adminConnectionString = Environment.GetEnvironmentVariable("ELCARBONAZO_TEST_ADMIN_CONNECTION");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            var developmentSettings = FindDevelopmentSettings();
            if (developmentSettings is null)
            {
                throw new InvalidOperationException(
                    "Configure ELCARBONAZO_TEST_ADMIN_CONNECTION to a PostgreSQL administrator connection or run tests from the repository root with local Development settings.");
            }

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(developmentSettings));
            adminConnectionString = document.RootElement
                .GetProperty("ConnectionStrings")
                .GetProperty("DefaultConnection")
                .GetString();
        }

        var adminBuilder = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = "postgres" };
        _adminConnectionString = adminBuilder.ConnectionString;
        ConnectionString = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString)
        {
            Database = _databaseName
        }.ConnectionString;

        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin))
        {
            await create.ExecuteNonQueryAsync();
        }

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync(migrationTarget);
            var passwordHasher = new BCryptPasswordHasher(Options.Create(new ElCarbonazo.Api.Configuration.PasswordHashingOptions
            {
                WorkFactor = 10
            }));
            var auditRecorder = new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System);
            await new AdminBootstrapService(
                    context,
                    passwordHasher,
                    new TestHostEnvironment(Environments.Production),
                    auditRecorder)
                .BootstrapAsync("Jefa Central", "jefa@example.local", "correct horse battery staple", CancellationToken.None);

            var kitchenHash = passwordHasher.Hash("kitchen password");
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO usuario (id_usuario, nombre, contrasena_hash, rol, email, activo)
                VALUES (2, 'cocina', {kitchenHash}, 'Personal de Cocina', 'cocina@example.local', true)
                """);
        }
    }

    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString)
        .Options);

    private static string? FindDevelopmentSettings()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "src", "ElCarbonazo.Api", "appsettings.Development.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    public async Task DisposeAsync()
    {
        if (_adminConnectionString is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_adminConnectionString);
            await admin.OpenAsync();
            await using (var terminate = new NpgsqlCommand("""
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @databaseName AND pid <> pg_backend_pid()
                """, admin))
            {
                terminate.Parameters.AddWithValue("databaseName", _databaseName);
                await terminate.ExecuteNonQueryAsync();
            }

            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\"", admin);
            await drop.ExecuteNonQueryAsync();
        }

        if (Directory.Exists(LogDirectory))
        {
            Directory.Delete(LogDirectory, recursive: true);
        }
    }
}

internal sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "ElCarbonazo.Api.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}

internal sealed class TestHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }
}
