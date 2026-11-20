using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;

/// <summary>Prueba de integración en loopback, sin ventana ni render.</summary>
internal static class PedidosSmoke
{
    public static async Task RunAsync(HttpClient client, CancellationToken token)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var login = await client.PostAsJsonAsync("api/auth/login",
            new { nombre = "jefa central", contrasena = "correct horse battery staple" }, token);
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthSessionResponse>(cancellationToken: token)
            ?? throw new InvalidDataException("No se recibió una sesión de prueba válida.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var session = new LoopbackSession(client);
        var products = await new ProductosPedidoApiService(session).ConsultarAsync(cancellationToken: token);
        if (products.Items.Count != 10) throw new InvalidDataException("Catálogo temporal incompleto.");
        var product = products.Items[0];
        var mesas = await client.GetFromJsonAsync<JsonElement[]>("api/mesas", token)
            ?? throw new InvalidDataException("No se recibieron mesas.");
        var table = mesas.Single(item => item.GetProperty("numero").GetInt32() == 4);
        var mesaId = table.GetProperty("idMesa").GetInt32();
        var version = table.GetProperty("version").GetUInt32();
        var accounts = new CuentaPedidoApiService(session);
        if ((await accounts.ConsultarAsync(mesaId, null, token)).Pedido is not null)
            throw new InvalidDataException("La mesa temporal para pedidos no estaba vacía.");
        var request = new IntencionPedido(mesaId, null, version,
            [new LineaIntencionPedido(product.IdProducto, product.Version, 2, "Sin sal", product.Nombre, product.Precio)]);
        var sender = new EnvioPedidoApiService(session);
        var sent = await sender.EnviarAsync(request, token);
        var repeated = await sender.EnviarAsync(request, token);
        if (sent.IdComanda != repeated.IdComanda) throw new InvalidDataException("La repetición creó otra comanda.");
        var account = (await accounts.ConsultarAsync(mesaId, null, token)).Pedido
            ?? throw new InvalidDataException("No se recuperó la cuenta confirmada.");
        if (account.Comandas.Count != 1 || account.Total != 2 * product.Precio || account.Impuestos != 0)
            throw new InvalidDataException("La cuenta temporal no coincide con el envío.");

        var memberTables = mesas.Where(item => item.GetProperty("numero").GetInt32() is 5 or 6)
            .Select(item => new { idMesa = item.GetProperty("idMesa").GetInt32(), version = item.GetProperty("version").GetUInt32() }).ToArray();
        using var create = await client.PostAsJsonAsync("api/grupos-mesa", new { mesas = memberTables }, token);
        create.EnsureSuccessStatusCode();
        var groupId = (await create.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token)).GetProperty("idGrupo").GetInt32();
        var groupRequest = new IntencionPedido(null, groupId, null,
            [new LineaIntencionPedido(product.IdProducto, product.Version, 1, "", product.Nombre, product.Precio)]);
        var groupSent = await sender.EnviarAsync(groupRequest, token);
        var groupAccount = (await accounts.ConsultarAsync(null, groupId, token)).Pedido;
        if (groupAccount?.IdGrupo != groupId || groupAccount.Comandas.Count != 1 || groupSent.Pedido.IdMesa is not null)
            throw new InvalidDataException("La cuenta temporal de grupo no coincide con el envío.");

        var kitchen = new CocinaApiService(session);
        var queue = await kitchen.ConsultarAsync(cancellationToken: token);
        var ticket = queue.Items.Single(item => item.IdComanda == sent.IdComanda);
        var prepared = await kitchen.CambiarEstadoAsync(ticket, token);
        var ready = await kitchen.CambiarEstadoAsync(prepared, token);
        if (ready.Estado != "Lista") throw new InvalidDataException("La comanda no quedó lista.");
        account = (await accounts.ConsultarAsync(mesaId, null, token)).Pedido;
        if (account?.Estado != "Listo" || account.Comandas.Single().Estado != "Lista")
            throw new InvalidDataException("La cuenta no reflejó la comanda lista.");
        Console.WriteLine("PEDIDOS SMOKE: catálogo, cuenta individual/grupo, envío idempotente y transición cocina verificados por REST en base temporal.");
        Console.WriteLine("Sesión adaptada de prueba; sin ventana, interacción humana ni render visual.");
    }

    private sealed class LoopbackSession(HttpClient client) : ISessionManager
    {
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged { add { } remove { } }
        public bool HasSession => true;
        public SessionLifecycleState State => SessionLifecycleState.Active;
        public SessionSnapshot? CurrentSession => null;
        public Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> factory, CancellationToken cancellationToken = default) => SendAsync(factory, cancellationToken);
        public Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> factory, bool isRead, CancellationToken cancellationToken = default) => SendAsync(factory, cancellationToken);
        private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> factory, CancellationToken token)
        {
            using var request = factory();
            return await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token);
        }
        public Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EvaluateState() { }
    }
}
