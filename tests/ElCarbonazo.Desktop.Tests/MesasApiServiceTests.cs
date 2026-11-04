using System.Net;
using System.Text;
using System.Text.Json;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class MesasApiServiceTests
{
    [Fact]
    public async Task GetUsesPollingAndReadsContract()
    {
        var session = new FakeSession(_ => Json(HttpStatusCode.OK,
            """[{"idMesa":1,"idGrupo":null,"numero":7,"capacidad":4,"estado":"Libre","version":4294967295}]"""));
        var mesas = await new MesasApiService(session).GetAsync();

        Assert.True(session.WasPolling);
        Assert.Equal("api/mesas", session.Path);
        Assert.Equal(7, Assert.Single(mesas).Numero);
        Assert.Equal(uint.MaxValue, mesas[0].Version);
        Assert.Null(mesas[0].IdGrupo);
    }

    [Fact]
    public async Task UpdateUsesHumanRequestAndCurrentVersion()
    {
        var session = new FakeSession(_ => Json(HttpStatusCode.OK,
            """{"idMesa":1,"idGrupo":null,"numero":7,"capacidad":4,"estado":"Reservada","version":8}"""));
        var mesa = await new MesasApiService(session).UpdateEstadoAsync(1, "Reservada", 7);

        Assert.False(session.WasPolling);
        Assert.Equal(HttpMethod.Put, session.Method);
        using var body = JsonDocument.Parse(session.Body!);
        Assert.Equal("Reservada", body.RootElement.GetProperty("estado").GetString());
        Assert.Equal(7u, body.RootElement.GetProperty("version").GetUInt32());
        Assert.Equal(8u, mesa.Version);
    }

    [Fact]
    public async Task ProblemDetailsPreservesStatusAndCode()
    {
        var session = new FakeSession(_ => Json(HttpStatusCode.Conflict,
            """{"status":409,"code":"MESA_CON_PEDIDO_PENDIENTE","detail":"Cuenta pendiente"}"""));
        var error = await Assert.ThrowsAsync<ApiException>(() =>
            new MesasApiService(session).UpdateEstadoAsync(1, "Libre", 7));

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal("MESA_CON_PEDIDO_PENDIENTE", error.Problem?.Code);
    }

    [Theory]
    [InlineData("[]", false)]
    [InlineData("[null]", true)]
    [InlineData("[{\"idMesa\":1,\"numero\":1,\"capacidad\":4,\"estado\":\"Libre\"},{\"idMesa\":1,\"numero\":2,\"capacidad\":4,\"estado\":\"Libre\"}]", true)]
    [InlineData("[{\"idMesa\":1,\"numero\":1,\"capacidad\":4,\"estado\":\"Agrupada\"}]", true)]
    public async Task GetRejectsIncoherentSnapshots(string body, bool invalid)
    {
        var service = new MesasApiService(new FakeSession(_ => Json(HttpStatusCode.OK, body)));
        if (invalid) await Assert.ThrowsAsync<InvalidDataException>(() => service.GetAsync());
        else Assert.Empty(await service.GetAsync());
    }

    [Fact]
    public async Task UpdateRejectsUnrelatedSuccessResponse()
    {
        var service = new MesasApiService(new FakeSession(_ => Json(HttpStatusCode.OK,
            """{"idMesa":2,"numero":2,"capacidad":4,"estado":"Ocupada","version":8}""")));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.UpdateEstadoAsync(1, "Ocupada", 7));
    }

    [Fact]
    public async Task CreateGroupSendsIdsAndVersions()
    {
        var session = new FakeSession(_ => Json(HttpStatusCode.Created,
            """{"idGrupo":5,"idUsuarioCreacion":1,"fechaCreacion":"2026-01-01T00:00:00Z","activo":true,"mesas":[]}"""));
        var group = await new GruposMesaApiService(session).CreateAsync(
            [new MesaVersionRequest(1, 10), new MesaVersionRequest(2, 20)]);

        Assert.Equal(5, group.IdGrupo);
        Assert.Equal(HttpMethod.Post, session.Method);
        using var body = JsonDocument.Parse(session.Body!);
        Assert.Equal(20u, body.RootElement.GetProperty("mesas")[1].GetProperty("version").GetUInt32());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class FakeSession(Func<HttpRequestMessage, HttpResponseMessage> response) : ISessionManager
    {
        public bool WasPolling { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        public HttpMethod? Method { get; private set; }
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged { add { } remove { } }
        public bool HasSession => true;
        public SessionLifecycleState State => SessionLifecycleState.Active;
        public SessionSnapshot? CurrentSession => null;
        public Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default) => Send(requestFactory, true);
        public Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default) => Send(requestFactory, false);
        private async Task<HttpResponseMessage> Send(Func<HttpRequestMessage> factory, bool polling)
        {
            using var request = factory();
            WasPolling = polling;
            Path = request.RequestUri?.ToString();
            Method = request.Method;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return response(request);
        }
        public Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EvaluateState() { }
    }
}
