using System.Net;
using System.Text;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class ProductosPedidoApiServiceTests
{
    [Fact]
    public async Task Account_reads_correct_group_without_treating_it_as_individual_table()
    {
        var session = new FakeSession("""
            {"pedido":{"idPedido":8,"idMesa":null,"idGrupo":3,"idUsuario":1,"fechaHora":"2026-01-01T00:00:00Z","subtotal":10.10,"impuestos":0,"total":10.10,"estado":"Listo","soloLectura":false,"motivo":null,"comandas":[{"idComanda":1,"idUsuario":1,"fechaHora":"2026-01-01T00:00:00Z","estado":"Lista","version":1,"lineas":[{"idDetalle":1,"idProducto":1,"nombreProducto":"DEMO","cantidad":1,"precioUnitario":10.10,"subtotal":10.10,"notas":""}]}]}}
            """);
        var response = await new CuentaPedidoApiService(session).ConsultarAsync(null, 3);
        Assert.Equal("api/pedidos/activo?grupoId=3", session.Path);
        Assert.Equal("Lista", Assert.Single(response.Pedido!.Comandas).Estado);
        await Assert.ThrowsAsync<InvalidDataException>(() => new CuentaPedidoApiService(session).ConsultarAsync(3, null));
    }

    [Fact]
    public async Task Empty_account_is_a_valid_result()
    {
        var response = await new CuentaPedidoApiService(new FakeSession("""{"pedido":null}""")).ConsultarAsync(1, null);
        Assert.Null(response.Pedido);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CuentaPedidoApiService(new FakeSession("{}")).ConsultarAsync(1, null));
    }

    [Fact]
    public async Task Catalog_uses_polling_and_preserves_opaque_cursor()
    {
        var session = new FakeSession("""
            {"items":[{"idProducto":7,"nombre":"Producto DEMO","categoria":"Comida","precio":10.10,"disponible":true,"version":3}],"cursorSiguiente":"abc-_"}
            """);
        var page = await new ProductosPedidoApiService(session).ConsultarAsync("anterior-_");
        Assert.Equal("api/productos?disponibles=true&limite=25&cursor=anterior-_", session.Path);
        Assert.Equal(7, Assert.Single(page.Items).IdProducto);
        Assert.Equal("abc-_", page.CursorSiguiente);
        Assert.Equal(10.10m, page.Items[0].Precio);
    }

    [Theory]
    [InlineData("""{"items":[{"idProducto":1,"nombre":"A","categoria":"Comida","precio":1,"disponible":false,"version":1}]}""")]
    [InlineData("""{"items":[{"idProducto":1,"nombre":"A","categoria":"Otro","precio":1,"disponible":true,"version":1}]}""")]
    [InlineData("""{"items":[{"idProducto":1,"nombre":"A","categoria":"Comida","precio":0,"disponible":true,"version":1}]}""")]
    [InlineData("""{"items":[{"idProducto":1,"nombre":"A","categoria":"Comida","precio":1,"disponible":true,"version":1},{"idProducto":1,"nombre":"B","categoria":"Comida","precio":2,"disponible":true,"version":1}]}""")]
    public async Task Catalog_rejects_invalid_data(string json)
    {
        var service = new ProductosPedidoApiService(new FakeSession(json));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ConsultarAsync());
    }

    private sealed class FakeSession(string json) : ISessionManager
    {
        public string? Path { get; private set; }
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged { add { } remove { } }
        public bool HasSession => true;
        public SessionLifecycleState State => SessionLifecycleState.Active;
        public SessionSnapshot? CurrentSession => null;
        public Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default)
        {
            using var request = requestFactory();
            Path = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
        public Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EvaluateState() { }
    }
}
