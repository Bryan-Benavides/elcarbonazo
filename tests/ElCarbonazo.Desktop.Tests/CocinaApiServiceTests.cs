using System.Net;
using System.Text;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class CocinaApiServiceTests
{
    private const string Ticket = """
        {"idComanda":1,"idPedido":2,"idMesa":4,"numeroMesa":4,"idGrupo":null,"idUsuario":3,"fechaHora":"2026-01-01T00:00:00Z","estado":"Abierta","version":17,"lineas":[{"idDetalle":1,"nombreProducto":"DEMO","cantidad":2,"notas":"Sin sal"}]}
        """;

    [Fact]
    public async Task Queue_uses_polling_and_preserves_cursor_and_notes()
    {
        var session = new FakeSession($$"""{"items":[{{Ticket}}],"cursorSiguiente":null}""");
        var page = await new CocinaApiService(session).ConsultarAsync("old-_");
        Assert.Equal("api/cocina/cola?limite=25&cursor=old-_", session.Path);
        Assert.Equal("Sin sal", Assert.Single(Assert.Single(page.Items).Lineas).Notas);
        Assert.Null(page.CursorSiguiente);
        Assert.False(session.Human);
    }

    [Fact]
    public async Task Transition_sends_opaque_version_once_and_validates_response()
    {
        var session = new FakeSession(Ticket.Replace("Abierta", "En Preparación", StringComparison.Ordinal));
        var ticket = new CocinaComandaDto(1, 2, 4, 4, null, 3, DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            "Abierta", 17, [new CocinaLineaDto(1, "DEMO", 2, "Sin sal")]);
        var result = await new CocinaApiService(session).CambiarEstadoAsync(ticket);
        Assert.Equal("En Preparación", result.Estado);
        Assert.True(session.Human);
        Assert.Equal("api/cocina/comandas/1/estado", session.Path);
        Assert.Contains("\"versionComanda\":17", session.Body, StringComparison.Ordinal);
        Assert.Equal(1, session.Calls);
    }

    [Fact]
    public async Task Invalid_or_duplicate_queue_data_is_rejected()
    {
        var service = new CocinaApiService(new FakeSession($$"""{"items":[{{Ticket}},{{Ticket}}],"cursorSiguiente":null}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ConsultarAsync());
        var invalid = new CocinaApiService(new FakeSession("""{"items":null,"cursorSiguiente":null}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => invalid.ConsultarAsync());
    }

    private sealed class FakeSession(string json) : ISessionManager
    {
        public string? Path { get; private set; }
        public string Body { get; private set; } = "";
        public bool Human { get; private set; }
        public int Calls { get; private set; }
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged { add { } remove { } }
        public bool HasSession => true;
        public SessionLifecycleState State => SessionLifecycleState.Active;
        public SessionSnapshot? CurrentSession => null;
        public Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default) => Send(requestFactory);
        public Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default)
        {
            Human = true;
            Assert.False(isRead);
            return Send(requestFactory);
        }
        private async Task<HttpResponseMessage> Send(Func<HttpRequestMessage> factory)
        {
            Calls++;
            using var request = factory();
            Path = request.RequestUri?.ToString();
            if (request.Content is not null) Body = await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
        public Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EvaluateState() { }
    }
}
