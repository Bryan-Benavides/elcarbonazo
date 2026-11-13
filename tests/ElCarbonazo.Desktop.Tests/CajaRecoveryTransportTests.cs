using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class CajaRecoveryTransportTests
{
    [Fact]
    public async Task Real_session_and_http_adapter_never_retry_write_and_keep_journal_until_reconciled()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"caja-http-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "journal.json");
        var installation = Guid.NewGuid();
        try
        {
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1,
                instalacion = installation,
                estado = "SinPendientes",
                operacion = (Guid?)null,
                propietario = (int?)null,
                tipo = (string?)null
            }));
            File.WriteAllText(path + ".lock", "");
            var api = new FakeApi();
            using var session = new SessionManager(api, new ServerClock(), new DesktopJsonLogger());
            await session.LoginAsync("jefa", "fake");
            var store = new CajaRecoveryStore(path, installation, new TestPolicy());
            var coordinator = new CajaRecoveryCoordinator(store, new CajaRecoveryHttpTransport(session), () => session.CurrentSession?.IdUsuario);
            var id = Guid.NewGuid();
            var result = await coordinator.BeginAsync(id, 1, "AbrirTurno", arguments: new { fondoInicial = 0, confirmado = true });
            Assert.Equal(CajaRecoveryOutcome.Uncertain, result);
            Assert.Equal(1, api.WriteCount);
            Assert.Equal(0, api.RefreshCount);
            Assert.Equal(id, store.Read()!.Operacion);
            Assert.Equal(CajaRecoveryOutcome.Prepared, await coordinator.RecoverAsync());
            Assert.False(await coordinator.ReconcileAsync());
            Assert.Equal(CajaRecoveryOutcome.Cancelled, await coordinator.CancelAsync());
            Assert.Equal("TerminalPorConciliar", store.Read()!.Estado);
            Assert.False(await coordinator.ReconcileAsync()); // no C3 business reconciliation exists
            Assert.Equal(id, store.Read()!.Operacion);
            Assert.Equal(2, api.WriteCount);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    private sealed class TestPolicy : ICajaRecoverySecurityPolicy { public bool IsProtected(string path) => true; }

    private sealed class FakeApi : IApiClientService
    {
        public event EventHandler<SecurityFailureEventArgs>? SecurityFailure { add { } remove { } }
        public int WriteCount { get; private set; }
        public int RefreshCount { get; private set; }
        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, ApiRequestKind requestKind, CancellationToken cancellationToken = default)
        {
            var path = request.RequestUri!.ToString();
            var now = DateTimeOffset.UtcNow;
            if (path == "api/auth/login")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new AuthSessionResponse("access", "refresh", now.AddMinutes(5), now.AddMinutes(15), now.AddHours(8), now, 1, "jefa", "Jefa"))
                });
            if (path == "api/auth/refresh") { RefreshCount++; throw new InvalidOperationException("Unexpected refresh"); }
            Assert.NotNull(request.Headers.Authorization);
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { estado = "Preparada" }) });
            WriteCount++;
            if (path.EndsWith("/cancelacion", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { estado = "Cancelada" }) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = JsonContent.Create(new { code = "AUTH_ACCESS_TOKEN_EXPIRADO" })
            });
        }
    }
}
