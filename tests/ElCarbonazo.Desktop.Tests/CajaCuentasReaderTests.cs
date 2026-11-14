using System.Net;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class CajaCuentasReaderTests
{
    private const string Page = """
        {"items":[{"idPedido":1,"idMesa":1,"numeroMesa":1,"idGrupo":null,"estado":"Listo","subtotal":10,"impuestos":0,"total":10,
        "cobrable":true,"motivo":null,"huellaLineas":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","comandas":[{"idComanda":3,"estado":"Lista","version":1,"lineas":[{"idDetalle":4,"idProducto":5,"nombreProducto":"Comida","cantidad":1,"precioUnitario":10,"subtotal":10}]}]},
        {"idPedido":2,"idMesa":null,"numeroMesa":null,"idGrupo":5,"estado":"Abierto","subtotal":0,"impuestos":0,"total":0,
        "cobrable":false,"motivo":"CUENTA_LEGADA_SIN_COMANDAS","huellaLineas":null,"comandas":[]}],"cursorSiguiente":"abc"}
        """;

    [Fact]
    public async Task Displays_eligible_and_blocked_accounts_with_historical_lines()
    {
        var session = new FakeSender(Page);
        var page = await new CajaCuentasReader(session).ReadAsync(1, null, default);
        Assert.NotNull(page);
        Assert.Equal("abc", page.CursorSiguiente);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("Mesa 01", page.Items[0].Destino);
        Assert.True(page.Items[0].Cobrable);
        Assert.Contains(page.Items[0].Detalle, x => x.Texto.Contains("Comida", StringComparison.Ordinal));
        Assert.False(page.Items[1].Cobrable);
        Assert.Contains("sin comandas", page.Items[1].Explicacion, StringComparison.Ordinal);
        Assert.Equal("api/caja/cuentas?limite=25", session.LastPath);
    }

    [Theory]
    [InlineData("missing-cursor")]
    [InlineData("missing-reason")]
    [InlineData("duplicate-id")]
    [InlineData("missing-detail")]
    [InlineData("missing-hash")]
    public async Task Rejects_incomplete_or_ambiguous_pages(string variant)
    {
        var json = variant switch
        {
            "missing-cursor" => Page.Replace(",\"cursorSiguiente\":\"abc\"", "", StringComparison.Ordinal),
            "missing-reason" => Page.Replace(",\"motivo\":null", "", StringComparison.Ordinal),
            "duplicate-id" => Page.Replace("\"idPedido\":2", "\"idPedido\":1", StringComparison.Ordinal),
            "missing-hash" => Page.Replace("\"huellaLineas\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"", "\"huellaLineas\":null", StringComparison.Ordinal),
            _ => Page.Replace("\"lineas\":[{\"idDetalle\":4,\"idProducto\":5,\"nombreProducto\":\"Comida\",\"cantidad\":1,\"precioUnitario\":10,\"subtotal\":10}]", "\"lineas\":null", StringComparison.Ordinal)
        };
        Assert.Null(await new CajaCuentasReader(new FakeSender(json)).ReadAsync(1, null, default));
    }

    private sealed class FakeSender(string body) : ICajaSessionSender
    {
        public string? LastPath { get; private set; }
        public Task<HttpResponseMessage> SendCajaOnceAsync(int expectedActor, Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default)
        {
            Assert.True(isRead);
            using var request = requestFactory();
            Assert.Equal(HttpMethod.Get, request.Method);
            LastPath = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
