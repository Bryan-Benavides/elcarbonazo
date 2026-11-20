using System.Net;
using System.Text.Json;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class AnulacionCoordinatorTests
{
    [Fact]
    public async Task Successful_flow_registers_before_sending_and_keeps_no_secrets()
    {
        using var fixture = new JournalFixture();
        var api = new FakeAnulacionApi
        {
            OnAuthorize = _ =>
            {
                Assert.Equal("Pendiente", fixture.Journal.Read()?.Estado);
                return Task.FromResult(new AutorizarAnulacionResponse("token-efimero", DateTimeOffset.UtcNow.AddMinutes(2)));
            },
            OnExecute = _ => Task.FromResult(new ResultadoAnulacionDto("Completada", "ANULACION_COMPLETADA", 7, 10.10m, 0, 0, 11))
        };
        var intention = fixture.Intention(motive: "Pedido duplicado por error");
        var result = await fixture.Coordinator(api, actor: 7).AnularAsync(
            intention, "jefa@example.local", "correct horse battery staple");

        Assert.Equal(EstadoResultadoAnulacion.Completada, result.Estado);
        Assert.Equal(1, api.AuthorizeCount);
        Assert.Equal(1, api.ExecuteCount);
        Assert.Equal((1L, 7), (api.AuthorizeGeneration, api.AuthorizeActor));
        Assert.Equal((1L, 7), (api.ExecuteGeneration, api.ExecuteActor));
        Assert.Equal("TerminalPorConciliar", fixture.Journal.Read()?.Estado);
        Assert.Equal(intention.IdOperacion, fixture.Journal.Read()?.Operacion);
        var contents = File.ReadAllText(fixture.Path);
        Assert.DoesNotContain("Pedido duplicado", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("jefa@example.local", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("correct horse", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("token-efimero", contents, StringComparison.Ordinal);
        Assert.True(fixture.Journal.ClearAfterReconciliation(intention.IdOperacion, 7));
        Assert.Equal("SinPendientes", fixture.Journal.Read()?.Estado);
    }

    [Fact]
    public async Task Deterministic_rejection_is_terminal_and_reconciles()
    {
        using var fixture = new JournalFixture();
        var api = new FakeAnulacionApi
        {
            OnAuthorize = _ => Task.FromResult(new AutorizarAnulacionResponse("token", DateTimeOffset.UtcNow.AddMinutes(2))),
            OnExecute = _ => throw new ApiException(HttpStatusCode.Conflict,
                new ApiProblem(409, "CUENTA_YA_PAGADA", null, null, null))
        };
        var intention = fixture.Intention();
        var result = await fixture.Coordinator(api).AnularAsync(intention, "jefa@example.local", "clave");
        Assert.Equal(EstadoResultadoAnulacion.Rechazada, result.Estado);
        Assert.Contains("pagada", result.Mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("TerminalPorConciliar", fixture.Journal.Read()?.Estado);
        Assert.Equal(1, api.ExecuteCount);
        Assert.True(fixture.Coordinator(api).Conciliar(intention.IdOperacion));
    }

    [Fact]
    public async Task Uncertain_execution_never_reexecutes_during_recovery()
    {
        using var fixture = new JournalFixture();
        var api = new FakeAnulacionApi
        {
            OnAuthorize = _ => Task.FromResult(new AutorizarAnulacionResponse("token", DateTimeOffset.UtcNow.AddMinutes(2))),
            OnExecute = _ => throw new IOException("Respuesta perdida"),
            OnConsult = _ => Task.FromResult(new OperacionAnulacionDto("Autorizada", "OPERACION_AUTORIZADA", null)),
            OnBarrier = (_, _) => Task.FromResult(new OperacionAnulacionDto("Cancelada", "OPERACION_CANCELADA", "{}"))
        };
        var coordinator = fixture.Coordinator(api);
        var intention = fixture.Intention();
        var uncertain = await coordinator.AnularAsync(intention, "jefa@example.local", "clave");
        Assert.Equal(EstadoResultadoAnulacion.Incierta, uncertain.Estado);
        Assert.Equal("Pendiente", fixture.Journal.Read()?.Estado);
        Assert.Equal(1, api.ExecuteCount);

        var blocked = await coordinator.AnularAsync(fixture.Intention(), "jefa@example.local", "clave");
        Assert.Equal("OPERACION_PENDIENTE", blocked.Codigo);
        Assert.Equal(1, api.AuthorizeCount);
        Assert.Equal(1, api.ExecuteCount);

        var consult = await coordinator.RecuperarAsync(false);
        Assert.Equal(EstadoResultadoAnulacion.Incierta, consult.Estado);
        Assert.Equal(1, api.ConsultCount);
        Assert.Equal(1, api.ExecuteCount);

        var barrier = await coordinator.RecuperarAsync(true);
        Assert.Equal(EstadoResultadoAnulacion.Rechazada, barrier.Estado);
        Assert.Equal(1, api.BarrierCount);
        Assert.True(api.BarrierAjena is false);
        Assert.Equal(1, api.ExecuteCount);
        Assert.Equal("TerminalPorConciliar", fixture.Journal.Read()?.Estado);
    }

    [Fact]
    public async Task Lost_response_with_completed_operation_reconciles_without_repeating()
    {
        using var fixture = new JournalFixture();
        var api = new FakeAnulacionApi
        {
            OnAuthorize = _ => Task.FromResult(new AutorizarAnulacionResponse("token", DateTimeOffset.UtcNow.AddMinutes(2))),
            OnExecute = _ => throw new HttpRequestException("Respuesta perdida"),
            OnConsult = _ => Task.FromResult(new OperacionAnulacionDto("Completada", "ANULACION_COMPLETADA", "{}"))
        };
        var coordinator = fixture.Coordinator(api);
        var intention = fixture.Intention();
        Assert.Equal(EstadoResultadoAnulacion.Incierta,
            (await coordinator.AnularAsync(intention, "jefa@example.local", "clave")).Estado);
        var recovered = await coordinator.RecuperarAsync(false);
        Assert.Equal(EstadoResultadoAnulacion.Completada, recovered.Estado);
        Assert.Equal(1, api.ExecuteCount);
        Assert.Equal("TerminalPorConciliar", fixture.Journal.Read()?.Estado);
    }

    [Fact]
    public async Task Barrier_losing_the_race_reports_the_execution_result()
    {
        using var fixture = new JournalFixture();
        var api = new FakeAnulacionApi
        {
            OnAuthorize = _ => Task.FromResult(new AutorizarAnulacionResponse("token", DateTimeOffset.UtcNow.AddMinutes(2))),
            OnExecute = _ => throw new IOException("Respuesta perdida"),
            OnConsult = _ => Task.FromResult(new OperacionAnulacionDto("Autorizada", "OPERACION_AUTORIZADA", null)),
            OnBarrier = (_, _) => Task.FromResult(new OperacionAnulacionDto("Completada", "ANULACION_COMPLETADA", "{}"))
        };
        var coordinator = fixture.Coordinator(api);
        var intention = fixture.Intention();
        await coordinator.AnularAsync(intention, "jefa@example.local", "clave");
        var result = await coordinator.RecuperarAsync(true);
        Assert.Equal(EstadoResultadoAnulacion.Completada, result.Estado);
        Assert.Contains("ya había ganado", result.Mensaje, StringComparison.Ordinal);
        Assert.Equal(1, api.ExecuteCount);
    }

    [Fact]
    public async Task Session_change_before_execution_sends_no_execution()
    {
        using var fixture = new JournalFixture();
        long generation = 1;
        var api = new FakeAnulacionApi
        {
            OnAuthorize = _ =>
            {
                generation = 2;
                return Task.FromResult(new AutorizarAnulacionResponse("token", DateTimeOffset.UtcNow.AddMinutes(2)));
            },
            OnExecute = _ => Task.FromResult(new ResultadoAnulacionDto("Completada", "ANULACION_COMPLETADA"))
        };
        var coordinator = fixture.Coordinator(api, generation: () => generation);
        var result = await coordinator.AnularAsync(fixture.Intention(), "jefa@example.local", "clave");
        Assert.Equal(EstadoResultadoAnulacion.Incierta, result.Estado);
        Assert.Equal("AUTH_CAMBIO_SESION", result.Codigo);
        Assert.Equal(1, api.AuthorizeCount);
        Assert.Equal(0, api.ExecuteCount);
        Assert.Equal("Pendiente", fixture.Journal.Read()?.Estado);
    }

    [Fact]
    public async Task Missing_journal_fails_closed_without_sending_credentials()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"anulacion-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var installation = Guid.NewGuid();
            var journal = new AnulacionJournal(Path.Combine(directory, "anulaciones-journal.json"),
                installation, new TestPolicy());
            var api = new FakeAnulacionApi();
            var coordinator = new AnulacionCoordinator(journal, api, () => 7, () => 1, () => false);
            Assert.Null(journal.Read());
            var result = await coordinator.AnularAsync(new IntencionAnulacion(Guid.NewGuid(), 1, 1, 1, "Motivo válido"),
                "jefa@example.local", "clave");
            Assert.Equal("DIARIO_NO_DISPONIBLE", result.Codigo);
            Assert.Equal(0, api.AuthorizeCount);
            Assert.Equal(0, api.ExecuteCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Foreign_pending_operation_requires_jefa_and_audited_intervention()
    {
        using var fixture = new JournalFixture();
        var operation = Guid.NewGuid();
        Assert.True(fixture.Journal.Begin(operation, 42));
        var api = new FakeAnulacionApi
        {
            OnConsult = _ => throw new ApiException(HttpStatusCode.NotFound,
                new ApiProblem(404, "RECURSO_NO_ENCONTRADO", null, null, null)),
            OnBarrier = (_, ajena) => Task.FromResult(new OperacionAnulacionDto("Cancelada", "OPERACION_CANCELADA", "{}"))
        };
        var nonJefa = fixture.Coordinator(api, actor: 7);
        Assert.True(nonJefa.OperacionAjena());
        var denied = await nonJefa.RecuperarAsync(true);
        Assert.Equal("INTERVENCION_REQUERIDA", denied.Codigo);
        Assert.Equal(0, api.ConsultCount);

        var jefa = fixture.Coordinator(api, actor: 7, jefa: true);
        var result = await jefa.RecuperarAsync(true);
        Assert.Equal(EstadoResultadoAnulacion.Rechazada, result.Estado);
        Assert.Equal(1, api.BarrierCount);
        Assert.True(api.BarrierAjena);
        Assert.Equal("TerminalPorConciliar", fixture.Journal.Read()?.Estado);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("     ")]
    [InlineData("linea\u0001invalida")]
    public async Task Invalid_motive_never_reaches_the_api(string motive)
    {
        using var fixture = new JournalFixture();
        var api = new FakeAnulacionApi();
        var result = await fixture.Coordinator(api).AnularAsync(
            fixture.Intention(motive: motive), "jefa@example.local", "clave");
        Assert.Equal("VALIDATION_ERROR", result.Codigo);
        Assert.Equal(0, api.AuthorizeCount);
        Assert.Equal("SinPendientes", fixture.Journal.Read()?.Estado);
    }

    [Fact]
    public async Task Invalid_email_never_reaches_the_api()
    {
        using var fixture = new JournalFixture();
        var api = new FakeAnulacionApi();
        var result = await fixture.Coordinator(api).AnularAsync(fixture.Intention(), " ", "clave");
        Assert.Equal("VALIDATION_ERROR", result.Codigo);
        Assert.Equal(0, api.AuthorizeCount);
    }

    private sealed class FakeAnulacionApi : IAnulacionApiService
    {
        public Func<AutorizarAnulacionRequest, Task<AutorizarAnulacionResponse>>? OnAuthorize { get; init; }
        public Func<EjecutarAnulacionRequest, Task<ResultadoAnulacionDto>>? OnExecute { get; init; }
        public Func<Guid, Task<OperacionAnulacionDto>>? OnConsult { get; init; }
        public Func<Guid, bool, Task<OperacionAnulacionDto>>? OnBarrier { get; init; }
        public int AuthorizeCount { get; private set; }
        public int ExecuteCount { get; private set; }
        public int ConsultCount { get; private set; }
        public int BarrierCount { get; private set; }
        public long AuthorizeGeneration { get; private set; }
        public long ExecuteGeneration { get; private set; }
        public int AuthorizeActor { get; private set; }
        public int ExecuteActor { get; private set; }
        public bool? BarrierAjena { get; private set; }

        public Task<AutorizarAnulacionResponse> AutorizarAsync(long expectedGeneration,
            AutorizarAnulacionRequest request, CancellationToken cancellationToken = default)
        {
            AuthorizeCount++;
            AuthorizeGeneration = expectedGeneration;
            AuthorizeActor = 7;
            return (OnAuthorize ?? throw new InvalidOperationException("Autorización no configurada."))(request);
        }

        public Task<ResultadoAnulacionDto> EjecutarAsync(long expectedGeneration, int idPedido,
            EjecutarAnulacionRequest request, CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            ExecuteGeneration = expectedGeneration;
            ExecuteActor = 7;
            return (OnExecute ?? throw new InvalidOperationException("Ejecución no configurada."))(request);
        }

        public Task<OperacionAnulacionDto> ConsultarAsync(Guid idOperacion, CancellationToken cancellationToken = default)
        {
            ConsultCount++;
            return (OnConsult ?? throw new InvalidOperationException("Consulta no configurada."))(idOperacion);
        }

        public Task<OperacionAnulacionDto> BarreraAsync(long expectedGeneration, Guid idOperacion,
            bool intervencionAjena, CancellationToken cancellationToken = default)
        {
            BarrierCount++;
            BarrierAjena = intervencionAjena;
            return (OnBarrier ?? throw new InvalidOperationException("Barrera no configurada."))(idOperacion, intervencionAjena);
        }
    }

    private sealed class JournalFixture : IDisposable
    {
        private readonly string _directory;
        private readonly Guid _installation = Guid.NewGuid();
        public string Path { get; }
        public AnulacionJournal Journal { get; }

        public JournalFixture()
        {
            _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"anulacion-coordinator-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "anulaciones-journal.json");
            File.WriteAllBytes(Path, JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1,
                instalacion = _installation,
                estado = "SinPendientes",
                operacion = (Guid?)null,
                propietario = (int?)null
            }));
            File.WriteAllText(Path + ".lock", "");
            Journal = new AnulacionJournal(Path, _installation, new TestPolicy());
        }

        public IntencionAnulacion Intention(string motive = "Motivo válido de prueba") =>
            new(Guid.NewGuid(), 31, 21, 11, motive);

        public AnulacionCoordinator Coordinator(FakeAnulacionApi api, int actor = 7, bool jefa = false,
            Func<long>? generation = null) =>
            new(Journal, api, () => actor, generation ?? (() => 1), () => jefa);

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); }
            catch (IOException) { }
        }
    }

    private sealed class TestPolicy : ICajaRecoverySecurityPolicy
    {
        public bool IsProtected(string path) => true;
    }
}
