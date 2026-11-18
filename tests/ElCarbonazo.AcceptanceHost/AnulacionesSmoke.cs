using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

/// <summary>Recorrido local de anulaciones 006 por REST; no prueba render ni interacción humana.</summary>
internal static class AnulacionesSmoke
{
    private const string Motivo = "Pedido duplicado por error";
    private const string EmailJefa = "jefa@example.local";
    private const string ClaveJefa = "correct horse battery staple";

    public static async Task RunAsync(HttpClient client, CancellationToken token)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var login = await client.PostAsJsonAsync("api/auth/login",
            new { nombre = "jefa central", contrasena = ClaveJefa }, token);
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            auth.GetProperty("accessToken").GetString() ?? throw new InvalidDataException("Token vacío."));

        var mesas = await client.GetFromJsonAsync<JsonElement[]>("api/mesas", token)
            ?? throw new InvalidDataException("No se recibieron mesas.");
        var products = await client.GetFromJsonAsync<JsonElement>("api/productos?limite=100", token);
        var product = products.GetProperty("items")[0];
        var idProducto = product.GetProperty("idProducto").GetInt32();
        var versionProducto = product.GetProperty("version").GetInt32();

        var first = await CrearCuentaAsync(client, mesas, 4, idProducto, versionProducto, token);
        var operacion = Guid.NewGuid();
        var tokenAnulacion = await AutorizarAsync(client, operacion, first, token);
        using var execution = await EjecutarAsync(client, first.IdPedido, operacion, first.IdComanda,
            first.IdDetalle, tokenAnulacion, token);
        if (execution.StatusCode != HttpStatusCode.OK)
            throw new InvalidDataException($"La anulación devolvió HTTP {(int)execution.StatusCode}.");
        var executed = await execution.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
        if (executed.GetProperty("estado").GetString() != "Completada" ||
            executed.GetProperty("montoRevertido").GetDecimal() != first.Total)
            throw new InvalidDataException("La anulación no revirtió el importe exacto de la línea.");
        var idAnulacion = executed.GetProperty("idAnulacion").GetInt32();

        using var replay = await EjecutarAsync(client, first.IdPedido, operacion, first.IdComanda,
            first.IdDetalle, tokenAnulacion, token);
        var replayed = await replay.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
        if (replay.StatusCode != HttpStatusCode.OK || replayed.GetProperty("idAnulacion").GetInt32() != idAnulacion ||
            replayed.GetProperty("montoRevertido").GetDecimal() != first.Total)
            throw new InvalidDataException("El replay no devolvió el mismo resultado terminal.");

        using var consult = await client.GetAsync($"api/anulaciones/operaciones/{operacion}", token);
        AssertOk(consult);
        var consulted = await consult.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
        if (consulted.GetProperty("estado").GetString() != "Completada")
            throw new InvalidDataException("La consulta no describió la operación completada.");

        using var emptyAccount = await client.GetAsync($"api/pedidos/activo?mesaId={first.IdMesa}", token);
        AssertOk(emptyAccount);
        var emptyBody = await emptyAccount.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
        if (emptyBody.GetProperty("pedido").ValueKind is not JsonValueKind.Null)
            throw new InvalidDataException("La cuenta totalmente anulada siguió visible como activa.");

        var second = await CrearCuentaAsync(client, mesas, 5, idProducto, versionProducto, token);
        var operacionBarrera = Guid.NewGuid();
        await AutorizarAsync(client, operacionBarrera, second, token);
        using var pending = await client.GetAsync($"api/anulaciones/operaciones/{operacionBarrera}", token);
        AssertOk(pending);
        var pendingState = await pending.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
        if (pendingState.GetProperty("estado").GetString() != "Autorizada")
            throw new InvalidDataException("La operación autorizada no quedó pendiente.");

        using var barrier = await client.PostAsJsonAsync($"api/anulaciones/operaciones/{operacionBarrera}/cancelacion",
            new { confirmaIntervencion = true, intervencionAjena = false }, token);
        AssertOk(barrier);
        var barrierState = await barrier.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
        if (barrierState.GetProperty("estado").GetString() != "Cancelada")
            throw new InvalidDataException("La barrera no quedó registrada como cancelación durable.");

        // Una barrera ganadora no debe alterar la cuenta aunque se intente ejecutar después.
        using var afterBarrier = await EjecutarAsync(client, second.IdPedido, operacionBarrera, second.IdComanda,
            second.IdDetalle, "token-no-emitido", token);
        if (afterBarrier.StatusCode != HttpStatusCode.OK)
            throw new InvalidDataException("La ejecución posterior a la barrera no devolvió el resultado durable.");
        using var activeAccount = await client.GetAsync($"api/pedidos/activo?mesaId={second.IdMesa}", token);
        AssertOk(activeAccount);
        var account = await activeAccount.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
        var line = account.GetProperty("pedido").GetProperty("comandas")[0].GetProperty("lineas")[0];
        if (line.GetProperty("idDetalle").GetInt32() != second.IdDetalle)
            throw new InvalidDataException("La barrera revirtió la línea que debía permanecer activa.");

        Console.WriteLine("ANULACIONES SMOKE: autorización superior, ejecución única, replay terminal, consulta, " +
            "filtrado de cuenta anulada y barrera durable verificados por REST en base temporal.");
        Console.WriteLine("Sesión y credenciales ficticias; sin render visual ni prueba LAN.");
    }

    private static async Task<(int IdMesa, int IdPedido, int IdComanda, int IdDetalle, decimal Total)> CrearCuentaAsync(
        HttpClient client, JsonElement[] mesas, int numeroMesa, int idProducto, int versionProducto, CancellationToken token)
    {
        var table = mesas.Single(x => x.GetProperty("numero").GetInt32() == numeroMesa);
        var mesaId = table.GetProperty("idMesa").GetInt32();
        using var send = await client.PostAsJsonAsync("api/pedidos/envios", new
        {
            envioId = Guid.NewGuid(),
            mesaId,
            versionMesa = table.GetProperty("version").GetUInt32(),
            lineas = new[] { new { idProducto, versionProducto, cantidad = 1, notas = "" } }
        }, token);
        if (send.StatusCode != HttpStatusCode.Created)
            throw new InvalidDataException($"El envío de preparación devolvió HTTP {(int)send.StatusCode}.");
        var confirmed = await send.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
        var idPedido = confirmed.GetProperty("pedido").GetProperty("idPedido").GetInt32();
        var idComanda = confirmed.GetProperty("idComanda").GetInt32();
        using var accountResponse = await client.GetAsync($"api/pedidos/activo?mesaId={mesaId}", token);
        AssertOk(accountResponse);
        var account = await accountResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
        var pedido = account.GetProperty("pedido");
        var line = pedido.GetProperty("comandas")[0].GetProperty("lineas")[0];
        return (mesaId, idPedido, idComanda, line.GetProperty("idDetalle").GetInt32(),
            line.GetProperty("subtotal").GetDecimal());
    }

    private static async Task<string> AutorizarAsync(HttpClient client,
        Guid operacion, (int IdMesa, int IdPedido, int IdComanda, int IdDetalle, decimal Total) cuenta,
        CancellationToken token)
    {
        using var response = await client.PostAsJsonAsync("api/seguridad/autorizar-anulacion", new
        {
            idOperacion = operacion,
            idPedido = cuenta.IdPedido,
            idComanda = cuenta.IdComanda,
            idDetalle = cuenta.IdDetalle,
            motivo = Motivo,
            emailJefa = EmailJefa,
            contrasena = ClaveJefa
        }, token);
        AssertOk(response);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
        return body.GetProperty("token").GetString()
            ?? throw new InvalidDataException("La autorización no devolvió token efímero.");
    }

    private static Task<HttpResponseMessage> EjecutarAsync(HttpClient client, int idPedido, Guid operacion,
        int idComanda, int idDetalle, string tokenAnulacion, CancellationToken token) =>
        client.PostAsJsonAsync($"api/pedidos/{idPedido}/anulaciones", new
        {
            idOperacion = operacion,
            idComanda,
            idDetalle,
            motivo = Motivo,
            token = tokenAnulacion
        }, token);

    private static void AssertOk(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidDataException($"Se esperaba HTTP 200 y se recibió {(int)response.StatusCode}.");
    }
}
