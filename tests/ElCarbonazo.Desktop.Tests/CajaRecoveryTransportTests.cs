using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class CajaRecoveryTransportTests
{
    [Theory]
    [InlineData("AbrirTurno")]
    [InlineData("CerrarTurno")]
    [InlineData("CobrarCuenta")]
    public async Task Tombstone_requires_explicit_barrier_and_cache_invalidation(string attemptedType)
    {
        var id = Guid.NewGuid();
        var evidence = $$"""{"idOperacion":"{{id}}","estado":"Cancelada","tipo":null,"idPedido":null,"idTurnoCaja":null,"sinIntencion":true}""";
        var sink = new SnapshotSink { Accept = true };
        var session = new EvidenceSession(HttpStatusCode.OK, "{\"estado\":\"Cancelada\"}", evidence, "{\"turno\":null}");
        Assert.True(await new CajaRecoveryHttpTransport(session, sink).ReconcileAsync(id, 1, attemptedType, default));
        Assert.Equal(attemptedType, sink.BarrierType);
        Assert.True(sink.BarrierApplied);
        Assert.Null(sink.Last);
    }

    [Theory]
    [InlineData("{\"turno\":null}", false)]
    [InlineData("{}", true)]
    public async Task Tombstone_stays_blocked_when_current_shift_or_consumer_fails(string shift, bool accept)
    {
        var id = Guid.NewGuid();
        var evidence = $$"""{"idOperacion":"{{id}}","estado":"Cancelada","tipo":null,"idPedido":null,"idTurnoCaja":null,"sinIntencion":true}""";
        var sink = new SnapshotSink { Accept = accept };
        Assert.False(await new CajaRecoveryHttpTransport(new EvidenceSession(HttpStatusCode.OK, "{\"estado\":\"Cancelada\"}", evidence, shift), sink)
            .ReconcileAsync(id, 1, "CobrarCuenta", default));
    }

    [Fact]
    public async Task Tombstone_journal_is_retained_until_barrier_snapshot_is_applied()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"caja-barrier-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "journal.json");
        var installation = Guid.NewGuid();
        var operation = Guid.NewGuid();
        try
        {
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1, instalacion = installation, estado = "SinPendientes", operacion = (Guid?)null,
                propietario = (int?)null, tipo = (string?)null
            }));
            File.WriteAllText(path + ".lock", "");
            var store = new CajaRecoveryStore(path, installation, new TestPolicy());
            Assert.True(store.Begin(operation, 1, "CobrarCuenta"));
            Assert.True(store.MarkTerminal(operation, 1));
            var evidence = $$"""{"idOperacion":"{{operation}}","estado":"Cancelada","tipo":null,"idPedido":null,"idTurnoCaja":null,"sinIntencion":true}""";
            CajaRecoveryCoordinator Coordinator(bool accept) => new(store, new CajaRecoveryHttpTransport(
                new EvidenceSession(HttpStatusCode.OK, "{\"estado\":\"Cancelada\"}", evidence, "{\"turno\":null}"),
                new SnapshotSink { Accept = accept }), () => 1);
            Assert.False(await Coordinator(false).ReconcileAsync());
            Assert.Equal(operation, store.Read()!.Operacion);
            Assert.True(await Coordinator(true).ReconcileAsync());
            Assert.Equal("SinPendientes", store.Read()!.Estado);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData("Rechazada", HttpStatusCode.Conflict)]
    [InlineData("Cancelada", HttpStatusCode.OK)]
    public async Task Unsuccessful_payment_applies_account_and_shift_together_after_pagination(string state, HttpStatusCode status)
    {
        var id = Guid.NewGuid();
        var evidence = $$"""{"idOperacion":"{{id}}","estado":"{{state}}","tipo":"CobrarCuenta","idPedido":3,"idTurnoCaja":17,"sinIntencion":false}""";
        const string first = "{\"items\":[{\"idPedido\":1}],\"cursorSiguiente\":\"abc\"}";
        const string second = "{\"items\":[{\"idPedido\":3,\"estado\":\"Listo\",\"total\":10,\"cobrable\":true,\"motivo\":null}],\"cursorSiguiente\":null}";
        const string shift = "{\"idTurnoCaja\":17,\"estado\":\"Abierto\",\"fondoInicial\":10,\"totalEfectivo\":0,\"totalTarjeta\":0,\"totalTransferencia\":0,\"efectivoEsperado\":10,\"cantidadCuentasPendientes\":1}";
        var sink = new SnapshotSink { Accept = true };
        var session = new EvidenceSession(status, $"{{\"estado\":\"{state}\"}}", evidence, first, second, shift);
        Assert.True(await new CajaRecoveryHttpTransport(session, sink).ReconcileAsync(id, 1, "CobrarCuenta", default));
        Assert.Equal(3, sink.Account!.IdPedido);
        Assert.Equal(17, sink.Last!.IdTurnoCaja);
        Assert.Equal(5, session.Calls);
    }

    [Theory]
    [InlineData("{\"items\":[],\"cursorSiguiente\":null}")]
    [InlineData("{\"items\":[{\"idPedido\":3,\"estado\":\"Listo\",\"total\":10,\"cobrable\":true}],\"cursorSiguiente\":null}")]
    [InlineData("{\"items\":[{\"idPedido\":3,\"estado\":\"Listo\",\"total\":10,\"cobrable\":true,\"motivo\":null},{\"idPedido\":3}],\"cursorSiguiente\":null}")]
    [InlineData("{\"items\":[{\"idPedido\":3,\"estado\":\"Listo\",\"total\":10,\"cobrable\":true,\"motivo\":null}],\"cursorSiguiente\":\"?\"}")]
    public async Task Unsuccessful_payment_does_not_apply_missing_or_ambiguous_account(string page)
    {
        var id = Guid.NewGuid();
        var evidence = $$"""{"idOperacion":"{{id}}","estado":"Cancelada","tipo":"CobrarCuenta","idPedido":3,"idTurnoCaja":17,"sinIntencion":false}""";
        var sink = new SnapshotSink { Accept = true };
        Assert.False(await new CajaRecoveryHttpTransport(new EvidenceSession(HttpStatusCode.OK, "{\"estado\":\"Cancelada\"}", evidence, page), sink)
            .ReconcileAsync(id, 1, "CobrarCuenta", default));
        Assert.Null(sink.Account);
        Assert.Null(sink.Last);
    }

    [Fact]
    public async Task Unsuccessful_payment_keeps_journal_when_business_read_fails()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"caja-payment-read-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "journal.json");
        var installation = Guid.NewGuid();
        var operation = Guid.NewGuid();
        try
        {
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1, instalacion = installation, estado = "SinPendientes", operacion = (Guid?)null,
                propietario = (int?)null, tipo = (string?)null
            }));
            File.WriteAllText(path + ".lock", "");
            var store = new CajaRecoveryStore(path, installation, new TestPolicy());
            Assert.True(store.Begin(operation, 1, "CobrarCuenta"));
            Assert.True(store.MarkTerminal(operation, 1));
            var evidence = $$"""{"idOperacion":"{{operation}}","estado":"Cancelada","tipo":"CobrarCuenta","idPedido":3,"idTurnoCaja":17,"sinIntencion":false}""";
            var coordinator = new CajaRecoveryCoordinator(store, new CajaRecoveryHttpTransport(
                new FailingBusinessSession(evidence), new SnapshotSink { Accept = true }), () => 1);
            Assert.False(await coordinator.ReconcileAsync());
            Assert.Equal("TerminalPorConciliar", store.Read()!.Estado);
            Assert.Equal(operation, store.Read()!.Operacion);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData("Rechazada", HttpStatusCode.Conflict, "{\"turno\":null}")]
    [InlineData("Cancelada", HttpStatusCode.OK, "{\"turno\":{\"idTurnoCaja\":17,\"estado\":\"Abierto\",\"fondoInicial\":10,\"totalEfectivo\":0,\"totalTarjeta\":0,\"totalTransferencia\":0,\"efectivoEsperado\":10,\"cantidadCuentasPendientes\":0}}")]
    public async Task Unsuccessful_opening_applies_explicit_current_shift(string state, HttpStatusCode status, string current)
    {
        var id = Guid.NewGuid();
        var evidence = $$"""{"idOperacion":"{{id}}","estado":"{{state}}","tipo":"AbrirTurno","idPedido":null,"idTurnoCaja":null,"sinIntencion":false}""";
        var sink = new SnapshotSink { Accept = true };
        var session = new EvidenceSession(status, $"{{\"estado\":\"{state}\"}}", evidence, current);
        Assert.True(await new CajaRecoveryHttpTransport(session, sink).ReconcileAsync(id, 1, "AbrirTurno", default));
        Assert.True(sink.CurrentApplied);
        Assert.Equal(current.Contains("null", StringComparison.Ordinal) ? null : 17, sink.Last?.IdTurnoCaja);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"turno\":{}}")]
    [InlineData("{\"turno\":null,\"turno\":null}")]
    [InlineData("{\"turno\":{\"idTurnoCaja\":17,\"estado\":\"Cerrado\",\"fondoInicial\":10,\"totalEfectivo\":0,\"totalTarjeta\":0,\"totalTransferencia\":0,\"efectivoEsperado\":10,\"cantidadCuentasPendientes\":0}}")]
    public async Task Unsuccessful_opening_rejects_missing_or_invalid_current_shift(string current)
    {
        var id = Guid.NewGuid();
        var evidence = $$"""{"idOperacion":"{{id}}","estado":"Cancelada","tipo":"AbrirTurno","idPedido":null,"idTurnoCaja":null,"sinIntencion":false}""";
        var sink = new SnapshotSink { Accept = true };
        Assert.False(await new CajaRecoveryHttpTransport(new EvidenceSession(HttpStatusCode.OK, "{\"estado\":\"Cancelada\"}", evidence, current), sink)
            .ReconcileAsync(id, 1, "AbrirTurno", default));
        Assert.False(sink.CurrentApplied);
    }

    [Theory]
    [InlineData("Rechazada", HttpStatusCode.Conflict)]
    [InlineData("Cancelada", HttpStatusCode.OK)]
    public async Task Unsuccessful_close_requires_matching_durable_evidence_and_applied_shift(string state, HttpStatusCode status)
    {
        var id = Guid.NewGuid();
        var evidence = $$"""{"idOperacion":"{{id}}","estado":"{{state}}","tipo":"CerrarTurno","idPedido":null,"idTurnoCaja":17,"sinIntencion":false}""";
        const string shift = "{\"idTurnoCaja\":17,\"estado\":\"Abierto\",\"fondoInicial\":10,\"totalEfectivo\":0,\"totalTarjeta\":0,\"totalTransferencia\":0,\"efectivoEsperado\":10,\"cantidadCuentasPendientes\":0}";
        var sink = new SnapshotSink { Accept = true };
        var session = new EvidenceSession(status, $"{{\"estado\":\"{state}\"}}", evidence, shift);
        Assert.True(await new CajaRecoveryHttpTransport(session, sink).ReconcileAsync(id, 1, "CerrarTurno", default));
        Assert.Equal(17, sink.Last!.IdTurnoCaja);
        Assert.Equal(3, session.Calls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-id")]
    [InlineData("wrong-state")]
    [InlineData("tombstone")]
    [InlineData("unexpected-order")]
    public async Task Unsuccessful_close_rejects_incomplete_or_ambiguous_evidence(string variant)
    {
        var id = Guid.NewGuid();
        var evidence = $$"""{"idOperacion":"{{id}}","estado":"Cancelada","tipo":"CerrarTurno","idPedido":null,"idTurnoCaja":17,"sinIntencion":false}""";
        evidence = variant switch
        {
            "missing" => evidence.Replace(",\"sinIntencion\":false", "", StringComparison.Ordinal),
            "wrong-id" => evidence.Replace(id.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal),
            "wrong-state" => evidence.Replace("Cancelada", "Rechazada", StringComparison.Ordinal),
            "tombstone" => evidence.Replace("\"sinIntencion\":false", "\"sinIntencion\":true", StringComparison.Ordinal),
            _ => evidence.Replace("\"idPedido\":null", "\"idPedido\":5", StringComparison.Ordinal)
        };
        var sink = new SnapshotSink { Accept = true };
        var session = new EvidenceSession(HttpStatusCode.OK, "{\"estado\":\"Cancelada\"}", evidence);
        Assert.False(await new CajaRecoveryHttpTransport(session, sink).ReconcileAsync(id, 1, "CerrarTurno", default));
        Assert.Null(sink.Last);
        Assert.Equal(2, session.Calls);
    }

    [Fact]
    public async Task Unsuccessful_close_keeps_journal_if_snapshot_fails_or_actor_changes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"caja-reconcile-close-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "journal.json");
        var installation = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var evidence = $$"""{"idOperacion":"{{operation}}","estado":"Cancelada","tipo":"CerrarTurno","idPedido":null,"idTurnoCaja":17,"sinIntencion":false}""";
        const string terminal = "{\"estado\":\"Cancelada\"}";
        const string shift = "{\"idTurnoCaja\":17,\"estado\":\"Abierto\",\"fondoInicial\":10,\"totalEfectivo\":0,\"totalTarjeta\":0,\"totalTransferencia\":0,\"efectivoEsperado\":10,\"cantidadCuentasPendientes\":0}";
        try
        {
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1, instalacion = installation, estado = "SinPendientes", operacion = (Guid?)null,
                propietario = (int?)null, tipo = (string?)null
            }));
            File.WriteAllText(path + ".lock", "");
            var store = new CajaRecoveryStore(path, installation, new TestPolicy());
            Assert.True(store.Begin(operation, 1, "CerrarTurno"));
            Assert.True(store.MarkTerminal(operation, 1));
            var actor = 1;
            var failure = new CajaRecoveryCoordinator(store, new CajaRecoveryHttpTransport(
                new EvidenceSession(HttpStatusCode.OK, terminal, evidence, shift), new SnapshotSink { Accept = false }), () => actor);
            Assert.False(await failure.ReconcileAsync());
            Assert.Equal(operation, store.Read()!.Operacion);
            var changed = new CajaRecoveryCoordinator(store, new CajaRecoveryHttpTransport(
                new EvidenceSession(HttpStatusCode.OK, terminal, evidence, shift), new SnapshotSink { Accept = true, OnShift = () => actor = 2 }), () => actor);
            Assert.False(await changed.ReconcileAsync());
            Assert.Equal(operation, store.Read()!.Operacion);
            actor = 1;
            var success = new CajaRecoveryCoordinator(store, new CajaRecoveryHttpTransport(
                new EvidenceSession(HttpStatusCode.OK, terminal, evidence, shift), new SnapshotSink { Accept = true }), () => actor);
            Assert.True(await success.ReconcileAsync());
            Assert.Equal("SinPendientes", store.Read()!.Estado);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task Reconcile_payment_requires_matching_operation_receipt_and_historical_lines()
    {
        var id = Guid.NewGuid();
        var terminal = "{\"estado\":\"Completada\",\"codigo\":\"COBRO_REGISTRADO\",\"idCobro\":4,\"numeroComprobante\":9,\"idPedido\":3,\"idTurnoCaja\":2,\"total\":10,\"metodoPago\":\"Efectivo\"}";
        var receipt = $$"""{"idCobro":4,"numeroComprobante":9,"idOperacion":"{{id}}","idPedido":3,"idTurnoCaja":2,"idUsuario":1,"total":10,"subtotal":10,"impuestos":0,"metodoPago":"Efectivo","lineas":[{"orden":1,"nombreProducto":"Comida","cantidad":1,"precioUnitario":10,"subtotal":10}]}""";
        var sink = new SnapshotSink { Accept = true };
        Assert.True(await new CajaRecoveryHttpTransport(new SnapshotSession(terminal, receipt), sink)
            .ReconcileAsync(id, 1, "CobrarCuenta", default));
        Assert.Equal(9, sink.Payment!.NumeroComprobante);
        Assert.Single(sink.Payment.Lineas);
        var other = receipt.Replace(id.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal);
        var rejected = new SnapshotSink { Accept = true };
        Assert.False(await new CajaRecoveryHttpTransport(new SnapshotSession(terminal, other), rejected)
            .ReconcileAsync(id, 1, "CobrarCuenta", default));
        Assert.Null(rejected.Payment);
        var wrongTotal = receipt.Replace("\"subtotal\":10}", "\"subtotal\":9}", StringComparison.Ordinal);
        Assert.False(await new CajaRecoveryHttpTransport(new SnapshotSession(terminal, wrongTotal), rejected)
            .ReconcileAsync(id, 1, "CobrarCuenta", default));
    }

    [Fact]
    public async Task Journal_is_cleared_only_after_terminal_recheck_and_snapshot_application()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"caja-reconcile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "journal.json");
        var installation = Guid.NewGuid();
        var operation = Guid.NewGuid();
        const string terminal = "{\"estado\":\"Completada\",\"codigo\":\"TURNO_ABIERTO\",\"idTurnoCaja\":17}";
        const string shift = "{\"idTurnoCaja\":17,\"estado\":\"Abierto\",\"fondoInicial\":10,\"totalEfectivo\":0,\"totalTarjeta\":0,\"totalTransferencia\":0,\"efectivoEsperado\":10,\"cantidadCuentasPendientes\":0}";
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
            var store = new CajaRecoveryStore(path, installation, new TestPolicy());
            Assert.True(store.Begin(operation, 1, "AbrirTurno"));
            Assert.True(store.MarkTerminal(operation, 1));
            var failing = new CajaRecoveryCoordinator(store,
                new CajaRecoveryHttpTransport(new SnapshotSession(terminal, shift), new SnapshotSink { Accept = false }), () => 1);
            Assert.False(await failing.ReconcileAsync());
            Assert.Equal(operation, store.Read()!.Operacion);
            var success = new CajaRecoveryCoordinator(store,
                new CajaRecoveryHttpTransport(new SnapshotSession(terminal, shift), new SnapshotSink { Accept = true }), () => 1);
            Assert.True(await success.ReconcileAsync());
            Assert.Equal("SinPendientes", store.Read()!.Estado);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task Reconcile_shift_requires_terminal_operation_matching_snapshot_and_applied_state()
    {
        var id = Guid.NewGuid();
        var session = new SnapshotSession(
            "{\"estado\":\"Completada\",\"codigo\":\"TURNO_ABIERTO\",\"idTurnoCaja\":17}",
            "{\"idTurnoCaja\":17,\"estado\":\"Abierto\",\"fondoInicial\":10,\"totalEfectivo\":20,\"totalTarjeta\":5,\"totalTransferencia\":0,\"efectivoEsperado\":30,\"cantidadCuentasPendientes\":2}");
        var sink = new SnapshotSink { Accept = true };
        var transport = new CajaRecoveryHttpTransport(session, sink);
        Assert.True(await transport.ReconcileAsync(id, 1, "AbrirTurno", default));
        Assert.Equal(17, sink.Last!.IdTurnoCaja);
        Assert.Equal(30m, sink.Last.EfectivoEsperado);
        Assert.Equal(2, session.Calls);
    }

    [Theory]
    [InlineData("{\"estado\":\"Preparada\",\"codigo\":\"TURNO_ABIERTO\",\"idTurnoCaja\":17}")]
    [InlineData("{\"estado\":\"Completada\",\"codigo\":\"TURNO_CERRADO\",\"idTurnoCaja\":17}")]
    [InlineData("{\"estado\":\"Completada\",\"estado\":\"Completada\",\"codigo\":\"TURNO_ABIERTO\",\"idTurnoCaja\":17}")]
    public async Task Reconcile_never_accepts_prepared_wrong_or_ambiguous_terminal(string terminal)
    {
        var session = new SnapshotSession(terminal);
        var sink = new SnapshotSink { Accept = true };
        Assert.False(await new CajaRecoveryHttpTransport(session, sink).ReconcileAsync(Guid.NewGuid(), 1, "AbrirTurno", default));
        Assert.Null(sink.Last);
        Assert.Equal(1, session.Calls);
    }

    [Fact]
    public async Task Reconcile_refuses_inconsistent_shift_and_unapplied_snapshot()
    {
        const string terminal = "{\"estado\":\"Completada\",\"codigo\":\"TURNO_CERRADO\",\"idTurnoCaja\":17}";
        const string shift = "{\"idTurnoCaja\":17,\"estado\":\"Cerrado\",\"fondoInicial\":10,\"totalEfectivo\":20,\"totalTarjeta\":0,\"totalTransferencia\":0,\"efectivoEsperado\":30,\"cantidadCuentasPendientes\":0}";
        var rejected = new SnapshotSink { Accept = false };
        Assert.False(await new CajaRecoveryHttpTransport(new SnapshotSession(terminal, shift), rejected)
            .ReconcileAsync(Guid.NewGuid(), 1, "CerrarTurno", default));
        Assert.NotNull(rejected.Last);
        var inconsistent = shift.Replace("\"efectivoEsperado\":30", "\"efectivoEsperado\":31", StringComparison.Ordinal);
        var sink = new SnapshotSink { Accept = true };
        Assert.False(await new CajaRecoveryHttpTransport(new SnapshotSession(terminal, inconsistent), sink)
            .ReconcileAsync(Guid.NewGuid(), 1, "CerrarTurno", default));
        Assert.Null(sink.Last);
    }

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

    private sealed class SnapshotSink : ICajaRecoverySnapshotSink
    {
        public bool Accept { get; init; }
        public Action? OnShift { get; init; }
        public CajaTurnoSnapshot? Last { get; private set; }
        public CajaPagoSnapshot? Payment { get; private set; }
        public CajaCuentaRecoverySnapshot? Account { get; private set; }
        public string? BarrierType { get; private set; }
        public bool BarrierApplied { get; private set; }
        public Task<bool> ApplyBarrierAsync(string attemptedType, CajaTurnoSnapshot? currentShift, CancellationToken cancellationToken)
        {
            BarrierApplied = true;
            BarrierType = attemptedType;
            Last = currentShift;
            return Task.FromResult(Accept);
        }
        public bool CurrentApplied { get; private set; }
        public Task<bool> ApplyAccountAndShiftAsync(CajaCuentaRecoverySnapshot account, CajaTurnoSnapshot shift, CancellationToken cancellationToken)
        {
            Account = account;
            Last = shift;
            OnShift?.Invoke();
            return Task.FromResult(Accept);
        }
        public Task<bool> ApplyCurrentShiftAsync(CajaTurnoSnapshot? snapshot, CancellationToken cancellationToken)
        {
            CurrentApplied = true;
            Last = snapshot;
            OnShift?.Invoke();
            return Task.FromResult(Accept);
        }
        public Task<bool> ApplyShiftAsync(CajaTurnoSnapshot snapshot, CancellationToken cancellationToken)
        {
            Last = snapshot;
            OnShift?.Invoke();
            return Task.FromResult(Accept);
        }
        public Task<bool> ApplyPaymentAsync(CajaPagoSnapshot snapshot, CancellationToken cancellationToken)
        {
            Payment = snapshot;
            return Task.FromResult(Accept);
        }
    }

    private sealed class SnapshotSession(params string[] bodies) : ICajaSessionSender
    {
        public int Calls { get; private set; }
        public Task<HttpResponseMessage> SendCajaOnceAsync(int expectedActor, Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default)
        {
            Assert.True(isRead);
            using var request = requestFactory();
            Assert.Equal(HttpMethod.Get, request.Method);
            var body = bodies[Calls++];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class EvidenceSession(HttpStatusCode firstStatus, params string[] bodies) : ICajaSessionSender
    {
        public int Calls { get; private set; }
        public Task<HttpResponseMessage> SendCajaOnceAsync(int expectedActor, Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default)
        {
            Assert.True(isRead);
            using var request = requestFactory();
            Assert.Equal(HttpMethod.Get, request.Method);
            var index = Calls++;
            return Task.FromResult(new HttpResponseMessage(index == 0 ? firstStatus : HttpStatusCode.OK)
            { Content = new StringContent(bodies[index]) });
        }
    }

    private sealed class FailingBusinessSession(string evidence) : ICajaSessionSender
    {
        private int _calls;
        public Task<HttpResponseMessage> SendCajaOnceAsync(int expectedActor, Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default)
        {
            Assert.True(isRead);
            using var request = requestFactory();
            return ++_calls switch
            {
                1 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"estado\":\"Cancelada\"}") }),
                2 => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(evidence) }),
                _ => throw new HttpRequestException("Simulated business read failure")
            };
        }
    }

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
