using System.Text.Json;
using ElCarbonazo.Api.Configuration;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Npgsql;

internal sealed class TemporaryPostgresDatabase
{
    private readonly string _name = $"elcarbonazo_test_{Guid.NewGuid():N}";
    private string? _adminConnection;
    private bool _created;
    public string ConnectionString { get; private set; } = "";
    public string LogDirectory { get; } = Path.Combine(Path.GetTempPath(), $"elcarbonazo-acceptance-{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ELCARBONAZO_TEST_ADMIN_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ElCarbonazo.sln"))) directory = directory.Parent;
            var path = Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Ejecute la herramienta desde la solución."), "src", "ElCarbonazo.Api", "appsettings.Development.json");
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            configured = json.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString();
        }
        var admin = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
        _adminConnection = admin.ConnectionString;
        ConnectionString = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = _name }.ConnectionString;
        await using (var connection = new NpgsqlConnection(_adminConnection))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_name}\"", connection);
            await create.ExecuteNonQueryAsync();
            _created = true;
        }
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var hasher = new BCryptPasswordHasher(Options.Create(new PasswordHashingOptions { WorkFactor = 10 }));
        var owner = await context.Usuarios.SingleAsync();
        owner.Nombre = "jefa central";
        owner.Email = "jefa@example.local";
        owner.Rol = UserRoles.Jefa;
        owner.ContrasenaHash = hasher.Hash("correct horse battery staple");
        owner.Activo = true;
        context.Usuarios.Add(new Usuario { Nombre = "cocina", Email = "cocina@example.local", Rol = UserRoles.PersonalCocina, ContrasenaHash = hasher.Hash("kitchen password"), Activo = true });
        await context.SaveChangesAsync();
    }

    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task SeedDemoCatalogAsync()
    {
        if (!_created) throw new InvalidOperationException("La base temporal todavía no fue creada.");
        await using var context = CreateContext();
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DemoCatalog:Enabled"] = "true"
        }).Build();
        await new DemoCatalogSeeder(context, new AcceptanceEnvironment(), settings).SeedAsync(CancellationToken.None);
    }

    private sealed class AcceptanceEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ElCarbonazo.AcceptanceHost";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    public async Task DisposeAsync()
    {
        if (!_created || _adminConnection is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using (var terminate = new NpgsqlCommand("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid()", connection))
        {
            terminate.Parameters.AddWithValue("name", _name);
            await terminate.ExecuteNonQueryAsync();
        }
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{_name}\"", connection);
        await drop.ExecuteNonQueryAsync();
        // El directorio de logs temporal se conserva para inspección; no se eliminan rutas del usuario.
    }
}
