using System.Net;
using System.Text;
using System.Text.Json;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class EnvioPedidoApiServiceTests
{
    [Fact]
    public async Task Write_uses_human_session_and_validates_confirmed_lines()
    {
        var session = new FakeSession(ValidResponse());
        var intent = new IntencionPedido(1, null, 7, [new LineaIntencionPedido(3, 2, 2, "Café", "Prueba DEMO", 10.10m)]);
        var result = await new EnvioPedidoApiService(session).EnviarAsync(intent);
        Assert.Equal(1, session.Count);
        Assert.False(session.IsRead);
        using var body = JsonDocument.Parse(session.Body!);
        Assert.Equal(intent.EnvioId, body.RootElement.GetProperty("envioId").GetGuid());
        Assert.Equal(2, body.RootElement.GetProperty("lineas")[0].GetProperty("cantidad").GetInt32());
        Assert.False(body.RootElement.GetProperty("lineas")[0].TryGetProperty("precioUnitario", out _));
        Assert.Equal(20.20m, result.Pedido.Total);
    }

    [Fact]
    public async Task Incompatible_success_response_is_not_treated_as_confirmation()
    {
        var session = new FakeSession(ValidResponse().Replace("Prueba DEMO", "Otro producto", StringComparison.Ordinal));
        var intent = new IntencionPedido(1, null, 7, [new LineaIntencionPedido(3, 2, 2, "Café", "Prueba DEMO", 10.10m)]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new EnvioPedidoApiService(session).EnviarAsync(intent));
        Assert.Equal(1, session.Count);
    }

    private static string ValidResponse() => """
        {"idComanda":9,"versionMesa":8,"pedido":{"idPedido":1,"idMesa":1,"idGrupo":null,"idUsuario":1,"fechaHora":"2026-01-01T00:00:00Z","subtotal":20.20,"impuestos":0,"total":20.20,"estado":"Abierto","soloLectura":false,"motivo":null,"comandas":[{"idComanda":9,"idUsuario":1,"fechaHora":"2026-01-01T00:00:00Z","estado":"Abierta","version":1,"lineas":[{"idDetalle":5,"idProducto":3,"nombreProducto":"Prueba DEMO","cantidad":2,"precioUnitario":10.10,"subtotal":20.20,"notas":"Café"}]}]}}
        """;

    private sealed class FakeSession(string json) : ISessionManager
    {
        public int Count { get; private set; }
        public bool IsRead { get; private set; }
        public string? Body { get; private set; }
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged { add { } remove { } }
        public bool HasSession => true;
        public SessionLifecycleState State => SessionLifecycleState.Active;
        public SessionSnapshot? CurrentSession => null;
        public async Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default)
        {
            Count++;
            IsRead = isRead;
            using var request = requestFactory();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            response.Headers.Location = new Uri("/api/pedidos/activo?mesaId=1", UriKind.Relative);
            return response;
        }
        public Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EvaluateState() { }
    }
}
