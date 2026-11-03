using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
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
                Impuestos = 15m,
                Total = 115m,
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
                Impuestos = 15m,
                Total = 115m,
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

internal sealed class TestApiFactory(string connectionString, string logDirectory) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        builder.UseEnvironment(Environments.Development);
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

internal sealed class TemporaryPostgresDatabase : IAsyncLifetime
{
    private readonly string _databaseName = $"elcarbonazo_test_{Guid.NewGuid():N}";
    private string _adminConnectionString = null!;

    public string ConnectionString { get; private set; } = null!;
    public string LogDirectory { get; } = Path.Combine(Path.GetTempPath(), $"elcarbonazo-api-tests-{Guid.NewGuid():N}");

    public async Task InitializeAsync()
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
            await context.Database.MigrateAsync();
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
