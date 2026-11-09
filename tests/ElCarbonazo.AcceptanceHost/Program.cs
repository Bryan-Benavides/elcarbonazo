using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;

// Herramienta manual local: nunca utiliza la base de desarrollo como destino.
var root = FindRoot();
var apiDirectory = Path.Combine(root, "src", "ElCarbonazo.Api");
var apiDll = Path.Combine(apiDirectory, "bin", "Debug", "net10.0", "ElCarbonazo.Api.dll");
if (!File.Exists(apiDll)) throw new InvalidOperationException("Compile la solución antes de iniciar el entorno de aceptación.");

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
listener.Stop();
var baseUrl = $"http://127.0.0.1:{port}/";
var database = new TemporaryPostgresDatabase();
Process? api = null;
using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping.Cancel(); };
try
{
    await database.InitializeAsync();
    var pedidosMode = args.Contains("--pedidos", StringComparer.Ordinal) || args.Contains("--pedidos-smoke", StringComparer.Ordinal);
    if (pedidosMode) await database.SeedDemoCatalogAsync();
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = apiDirectory, UseShellExecute = false };
    start.ArgumentList.Add(apiDll);
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
    start.Environment["ASPNETCORE_URLS"] = baseUrl;
    start.Environment["ConnectionStrings__DefaultConnection"] = database.ConnectionString;
    start.Environment["Logging__FileDirectory"] = database.LogDirectory;
    api = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar la API temporal.");
    using var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
    await WaitForApiAsync(client, api, stopping.Token);
    await PreparePendingOrdersAsync(client, database, stopping.Token);
    Console.WriteLine($"\nENTORNO TEMPORAL LISTO: {baseUrl}");
    Console.WriteLine("Solo pruebas locales; los datos se eliminarán al detener este proceso.");
    Console.WriteLine("Jefa: jefa central / correct horse battery staple");
    Console.WriteLine("Cocina: cocina / kitchen password");
    Console.WriteLine("Mesa 1 ocupada con cuenta pendiente; mesas 2 y 3 agrupadas con cuenta pendiente.");
    if (pedidosMode) Console.WriteLine("Catálogo ficticio 003 cargado; para pedidos nuevos use la mesa 04 libre. Las cuentas 1 y 2/3 son heredadas de solo lectura.");
    Console.WriteLine("En otra terminal PowerShell, para cada Desktop:");
    Console.WriteLine("$env:ELCARBONAZO_ENVIRONMENT='Development'");
    Console.WriteLine($"$env:ELCARBONAZO_API_BASE_URL='{baseUrl}'");
    Console.WriteLine("dotnet run --project src/ElCarbonazo.Desktop");
    Console.WriteLine("Pulse Ctrl+C aquí para cerrar la API temporal y eliminar su base de prueba.");
    if (args.Contains("--pedidos-smoke", StringComparer.Ordinal))
        await PedidosSmoke.RunAsync(client, stopping.Token);
    else if (args.Contains("--cadencia", StringComparer.Ordinal))
        await CadenciaLocal.RunAsync(client, stopping.Token);
    else if (args.Contains("--medir", StringComparer.Ordinal))
        await MedirAsync(client, stopping.Token);
    else if (!args.Contains("--smoke", StringComparer.Ordinal))
        await Task.Delay(Timeout.Infinite, stopping.Token);
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
finally
{
    if (api is not null)
    {
        if (!api.HasExited) { api.Kill(entireProcessTree: true); await api.WaitForExitAsync(); }
        api.Dispose();
    }
    await database.DisposeAsync();
    Console.WriteLine("Entorno temporal finalizado.");
}

static string FindRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ElCarbonazo.sln"))) directory = directory.Parent;
        if (directory is not null) return directory.FullName;
    }
    throw new InvalidOperationException("No se encontró la raíz de la solución.");
}

static async Task WaitForApiAsync(HttpClient client, Process api, CancellationToken token)
{
    for (var attempt = 0; attempt < 100; attempt++)
    {
        token.ThrowIfCancellationRequested();
        if (api.HasExited) throw new InvalidOperationException("La API temporal terminó durante el inicio.");
        try
        {
            using var response = await client.GetAsync("api/mesas", token);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return;
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) when (!token.IsCancellationRequested) { }
        await Task.Delay(200, token);
    }
    throw new TimeoutException("La API temporal no respondió durante el inicio.");
}

static async Task PreparePendingOrdersAsync(HttpClient client, TemporaryPostgresDatabase database, CancellationToken token)
{
    using var login = await client.PostAsJsonAsync("api/auth/login", new { nombre = "jefa central", contrasena = "correct horse battery staple" }, token);
    login.EnsureSuccessStatusCode();
    using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync(token));
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginJson.RootElement.GetProperty("accessToken").GetString());
    var mesas = await client.GetFromJsonAsync<JsonElement[]>("api/mesas", token) ?? throw new InvalidDataException("No se recibieron mesas.");
    var mesa1 = mesas.Single(m => m.GetProperty("numero").GetInt32() == 1);
    var idMesa1 = mesa1.GetProperty("idMesa").GetInt32();
    using var ocupar = await client.PutAsJsonAsync($"api/mesas/{idMesa1}/estado", new { estado = "Ocupada", version = mesa1.GetProperty("version").GetUInt32() }, token);
    ocupar.EnsureSuccessStatusCode();
    var elegidas = mesas.Where(m => m.GetProperty("numero").GetInt32() is 2 or 3)
        .Select(m => new { idMesa = m.GetProperty("idMesa").GetInt32(), version = m.GetProperty("version").GetUInt32() }).ToArray();
    using var crear = await client.PostAsJsonAsync("api/grupos-mesa", new { mesas = elegidas }, token);
    crear.EnsureSuccessStatusCode();
    using var grupo = JsonDocument.Parse(await crear.Content.ReadAsStringAsync(token));
    var idGrupo = grupo.RootElement.GetProperty("idGrupo").GetInt32();
    await using var context = database.CreateContext();
    context.Pedidos.AddRange(
        new Pedido { IdMesa = idMesa1, IdUsuario = 1, FechaHora = DateTimeOffset.UtcNow, Subtotal = 100, Impuestos = 0, Total = 100, Estado = PedidoStates.Abierto },
        new Pedido { IdGrupo = idGrupo, IdUsuario = 1, FechaHora = DateTimeOffset.UtcNow, Subtotal = 100, Impuestos = 0, Total = 100, Estado = PedidoStates.Abierto });
    await context.SaveChangesAsync(token);
    using var logout = await client.PostAsync("api/auth/logout", null, token);
    logout.EnsureSuccessStatusCode();
    using var kitchen = await client.PostAsJsonAsync("api/auth/login", new { nombre = "cocina", contrasena = "kitchen password" }, token);
    kitchen.EnsureSuccessStatusCode();
    using var kitchenJson = JsonDocument.Parse(await kitchen.Content.ReadAsStringAsync(token));
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kitchenJson.RootElement.GetProperty("accessToken").GetString());
    using var kitchenRead = await client.GetAsync("api/mesas", token);
    kitchenRead.EnsureSuccessStatusCode();
    using var forbidden = await client.PutAsJsonAsync($"api/mesas/{idMesa1}/estado", new { estado = "Libre", version = 0 }, token);
    if (forbidden.StatusCode != HttpStatusCode.Forbidden) throw new InvalidDataException("Cocina no quedó configurada como solo lectura.");
    using var kitchenLogout = await client.PostAsync("api/auth/logout", null, token);
    kitchenLogout.EnsureSuccessStatusCode();
}

static async Task MedirAsync(HttpClient client, CancellationToken token)
{
    var autenticado = false;
    Console.WriteLine("MEDICIÓN: abra Desktop como Cocina y mantenga visible la mesa 04 junto a esta consola.");
    Console.WriteLine("La consola usa Jefa; abra Desktop únicamente como Cocina para no desplazar esta sesión.");
    Console.WriteLine("Grabe la pantalla. Pulse ESPACIO para cada muestra; se alterna Libre/Ocupada. Pulse Ctrl+C al terminar.");
    var muestra = 0;
    while (!token.IsCancellationRequested)
    {
        if (!Console.KeyAvailable) { await Task.Delay(100, token); continue; }
        if (Console.ReadKey(intercept: true).Key != ConsoleKey.Spacebar) continue;
        if (!autenticado)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("api/auth/login", new { nombre = "jefa central", contrasena = "correct horse battery staple" }, token);
            if (!login.IsSuccessStatusCode)
            {
                Console.WriteLine($"No se pudo iniciar la sesión de medición (HTTP {(int)login.StatusCode}). No se envió ningún cambio.");
                continue;
            }
            using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync(token));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginJson.RootElement.GetProperty("accessToken").GetString());
            autenticado = true;
            Console.WriteLine("Sesión de medición lista. Pulse ESPACIO otra vez para iniciar una muestra.");
            continue;
        }
        using var consulta = await client.GetAsync("api/mesas", token);
        if (consulta.StatusCode == HttpStatusCode.Unauthorized)
        {
            autenticado = false;
            Console.WriteLine($"Sesión de medición no válida ({await CodigoAsync(consulta, token)}). No se envió ningún cambio. Pulse ESPACIO para iniciar sesión nuevamente.");
            continue;
        }
        if (!consulta.IsSuccessStatusCode)
        {
            Console.WriteLine($"No se pudo consultar mesas (HTTP {(int)consulta.StatusCode}). No se envió ningún cambio.");
            continue;
        }
        var mesas = await consulta.Content.ReadFromJsonAsync<JsonElement[]>(cancellationToken: token) ?? throw new InvalidDataException("No se recibieron mesas.");
        var mesa = mesas.Single(m => m.GetProperty("numero").GetInt32() == 4);
        var estado = mesa.GetProperty("estado").GetString();
        var destino = estado switch { "Libre" => "Ocupada", "Ocupada" => "Libre", _ => throw new InvalidDataException("Mesa 04 no es elegible para la medición.") };
        var reloj = Stopwatch.StartNew();
        try
        {
            using var response = await client.PutAsJsonAsync($"api/mesas/{mesa.GetProperty("idMesa").GetInt32()}/estado",
                new { estado = destino, version = mesa.GetProperty("version").GetUInt32() }, token);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode >= 500)
                {
                    Console.WriteLine("Resultado incierto: la API respondió con un error de servidor y el cambio pudo aplicarse. No se reintentó. Revise la mesa y reinicie el entorno; un GET no prueba que la escritura haya terminado.");
                    await Task.Delay(Timeout.Infinite, token);
                    return;
                }
                if (response.StatusCode == HttpStatusCode.Unauthorized) autenticado = false;
                Console.WriteLine($"Muestra rechazada (HTTP {(int)response.StatusCode}, {await CodigoAsync(response, token)}). No se reintentó la escritura.");
                continue;
            }
            reloj.Stop();
            Console.WriteLine($"MUESTRA {++muestra}: API CONFIRMÓ {destino} | PUT {reloj.Elapsed.TotalMilliseconds:F0} ms | {DateTimeOffset.Now:HH:mm:ss.fff}");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !token.IsCancellationRequested)
        {
            Console.WriteLine("Resultado incierto: el cambio pudo aplicarse. No se reintentó. Revise la mesa y reinicie el entorno antes de continuar; un GET no prueba que la escritura haya terminado.");
            await Task.Delay(Timeout.Infinite, token);
        }
    }
}

static async Task<string> CodigoAsync(HttpResponseMessage response, CancellationToken token)
{
    try
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() ?? "sin código" : "sin código";
    }
    catch (JsonException) { return "sin código"; }
}
