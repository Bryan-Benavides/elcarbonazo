using System.Text.Json;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class CajaRecoveryViewModelTests
{
    [Fact]
    public async Task External_payment_session_change_during_execution_preserves_pending_operation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"caja-session-{Guid.NewGuid():N}");
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
            long generation = 1;
            var store = new CajaRecoveryStore(path, installation, new Policy());
            var vm = new CajaRecoveryViewModel(store, new FakeSession(), () => generation);
            var transport = new ExternalCollisionTransport(vm) { OnExecute = () => generation++ };
            vm.Attach(new CajaRecoveryCoordinator(store, transport, () => 1, sessionGeneration: () => generation));
            vm.AttachShiftReader(new OpenShiftReader());
            vm.AttachAccountsReader(new CashAccountsReader());
            Assert.True(vm.Open(() => { }));
            await vm.ConsultarTurnoCommand.ExecuteAsync(null);
            await vm.ConsultarCuentasCommand.ExecuteAsync(null);
            vm.SeleccionarCuentaCommand.Execute(vm.Cuentas[0]);
            vm.MetodoPago = "Transferencia";
            vm.ReferenciaPago = "DEMO";
            vm.ConfirmarCobro = true;
            vm.ConfirmarVerificacionExterna = true;
            await vm.CobrarExternoCommand.ExecuteAsync(null);
            Assert.Single(transport.Requests);
            Assert.Equal("Pendiente", store.Read()!.Estado);
            await vm.CobrarExternoCommand.ExecuteAsync(null);
            Assert.Single(transport.Requests);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData("Tarjeta")]
    [InlineData("Transferencia")]
    public async Task External_collision_requires_reconciliation_fresh_review_and_second_confirmation(string method)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"caja-external-{Guid.NewGuid():N}");
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
            var store = new CajaRecoveryStore(path, installation, new Policy());
            var vm = new CajaRecoveryViewModel(store, new FakeSession(), () => 1);
            var transport = new ExternalCollisionTransport(vm);
            vm.Attach(new CajaRecoveryCoordinator(store, transport, () => 1));
            vm.AttachShiftReader(new OpenShiftReader());
            vm.AttachAccountsReader(new CashAccountsReader());
            Assert.True(vm.Open(() => { }));
            await vm.ConsultarTurnoCommand.ExecuteAsync(null);
            await vm.ConsultarCuentasCommand.ExecuteAsync(null);
            vm.SeleccionarCuentaCommand.Execute(vm.Cuentas[0]);
            vm.MetodoPago = method;
            vm.ReferenciaPago = "  cafe\u0301  ";
            vm.ConfirmarCobro = true;
            await vm.CobrarExternoCommand.ExecuteAsync(null);
            Assert.Empty(transport.Requests);
            vm.ConfirmarVerificacionExterna = true;
            await vm.CobrarExternoCommand.ExecuteAsync(null);
            Assert.Single(transport.Requests);
            Assert.False(transport.Requests[0].Repeated);
            Assert.Equal("café", transport.Requests[0].Reference);
            Assert.Equal("TerminalPorConciliar", store.Read()!.Estado);
            await vm.ConciliarCommand.ExecuteAsync(null);
            Assert.Equal("SinPendientes", store.Read()!.Estado);
            await vm.ConsultarTurnoCommand.ExecuteAsync(null);
            await vm.ConsultarCuentasCommand.ExecuteAsync(null);
            vm.SeleccionarCuentaCommand.Execute(vm.Cuentas[0]);
            vm.MetodoPago = method;
            vm.ReferenciaPago = "café";
            vm.ConfirmarCobro = true;
            vm.ConfirmarVerificacionExterna = true;
            await vm.CobrarExternoCommand.ExecuteAsync(null);
            Assert.Single(transport.Requests);
            vm.ConfirmarReferenciaRepetida = true;
            vm.ReferenciaPago = "café-modificado";
            Assert.False(vm.ConfirmarReferenciaRepetida);
            Assert.False(vm.ConfirmarVerificacionExterna);
            vm.ReferenciaPago = "café";
            vm.ConfirmarCobro = true;
            vm.ConfirmarVerificacionExterna = true;
            vm.ConfirmarReferenciaRepetida = true;
            await vm.CobrarExternoCommand.ExecuteAsync(null);
            Assert.Equal(2, transport.Requests.Count);
            Assert.True(transport.Requests[1].Repeated);
            Assert.NotEqual(transport.Requests[0].Operation, transport.Requests[1].Operation);
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closing_requires_count_reason_and_confirmation_and_keeps_uncertain_journal(bool uncertain)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"caja-close-{Guid.NewGuid():N}");
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
            var store = new CajaRecoveryStore(path, installation, new Policy());
            var vm = new CajaRecoveryViewModel(store, new FakeSession(), () => 1);
            var transport = new ClosingTransport(vm, uncertain);
            vm.Attach(new CajaRecoveryCoordinator(store, transport, () => 1));
            vm.AttachShiftReader(new OpenShiftReader());
            Assert.True(vm.Open(() => { }));
            await vm.ConsultarTurnoCommand.ExecuteAsync(null);
            vm.EfectivoContado = "5.00";
            Assert.Contains("Sobrante", vm.DiferenciaCierre, StringComparison.Ordinal);
            vm.ConfirmarCierreCaja = true;
            await vm.CerrarTurnoCommand.ExecuteAsync(null);
            Assert.Equal(0, transport.Prepares);
            Assert.False(vm.ConfirmarCierreCaja);
            vm.MotivoCierre = "Conteo ficticio";
            await vm.CerrarTurnoCommand.ExecuteAsync(null);
            Assert.Equal(0, transport.Prepares);
            vm.ConfirmarCierreCaja = true;
            await vm.CerrarTurnoCommand.ExecuteAsync(null);
            Assert.Equal(1, transport.Prepares);
            Assert.Equal(1, transport.Executions);
            Assert.Equal(uncertain ? "Pendiente" : "SinPendientes", store.Read()!.Estado);
            if (!uncertain) Assert.Contains("Cerrado", vm.ResumenTurno, StringComparison.Ordinal);
            await vm.CerrarTurnoCommand.ExecuteAsync(null);
            Assert.Equal(1, transport.Executions);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Cash_payment_requires_selection_confirmation_and_never_retries_uncertain_execution(bool uncertain, bool reconciliationFails)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"caja-cash-{Guid.NewGuid():N}");
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
            var store = new CajaRecoveryStore(path, installation, new Policy());
            var vm = new CajaRecoveryViewModel(store, new FakeSession(), () => 1);
            var transport = new CashTransport(vm, uncertain, reconciliationFails);
            vm.Attach(new CajaRecoveryCoordinator(store, transport, () => 1));
            vm.AttachShiftReader(new OpenShiftReader());
            vm.AttachAccountsReader(new CashAccountsReader());
            Assert.True(vm.Open(() => { }));
            await vm.ConsultarTurnoCommand.ExecuteAsync(null);
            await vm.ConsultarCuentasCommand.ExecuteAsync(null);
            vm.SeleccionarCuentaCommand.Execute(vm.Cuentas[1]);
            Assert.Null(vm.CuentaSeleccionada);
            vm.SeleccionarCuentaCommand.Execute(vm.Cuentas[0]);
            Assert.NotNull(vm.CuentaSeleccionada);
            Assert.Equal("Efectivo", vm.MetodoPago);
            foreach (var method in new[] { "Tarjeta", "Transferencia" })
            {
                vm.ConfirmarCobro = true;
                vm.MetodoPago = method;
                Assert.False(vm.ConfirmarCobro);
                Assert.True(vm.EsPagoExterno);
                vm.ReferenciaPago = "  cafe\u0301  ";
                Assert.Equal("Referencia para revisión: café", vm.RevisionReferencia);
                vm.ConfirmarCobro = true;
                await vm.CobrarEfectivoCommand.ExecuteAsync(null);
                Assert.Equal(0, transport.Prepares);
                vm.ReferenciaPago = "inválida\nreferencia";
                Assert.False(vm.ConfirmarCobro);
                Assert.Contains("sin controles", vm.RevisionReferencia, StringComparison.Ordinal);
                vm.ReferenciaPago = new string('a', 101);
                Assert.Contains("1 a 100", vm.RevisionReferencia, StringComparison.Ordinal);
            }
            vm.MetodoPago = "Efectivo";
            Assert.Equal("", vm.ReferenciaPago);
            Assert.True(vm.EsEfectivo);
            vm.EfectivoRecibido = "9";
            vm.ConfirmarCobro = true;
            await vm.CobrarEfectivoCommand.ExecuteAsync(null);
            Assert.Equal(0, transport.Prepares);
            Assert.False(vm.ConfirmarCobro);
            vm.EfectivoRecibido = "15";
            await vm.CobrarEfectivoCommand.ExecuteAsync(null);
            Assert.Equal(0, transport.Prepares);
            vm.ConfirmarCobro = true;
            await vm.CobrarEfectivoCommand.ExecuteAsync(null);
            Assert.Equal(1, transport.Prepares);
            Assert.Equal(1, transport.Executions);
            Assert.Equal(uncertain ? "Pendiente" : reconciliationFails ? "TerminalPorConciliar" : "SinPendientes", store.Read()!.Estado);
            if (!uncertain && !reconciliationFails)
            {
                Assert.Contains("vuelto", vm.Fotografia, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Comida", vm.Fotografia, StringComparison.Ordinal);
                Assert.Contains("subtotal", vm.Fotografia, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("NO ES FACTURA FISCAL", vm.Fotografia, StringComparison.Ordinal);
            }
            await vm.CobrarEfectivoCommand.ExecuteAsync(null);
            Assert.Equal(1, transport.Executions);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task Account_read_is_discarded_when_session_changes()
    {
        long generation = 1;
        var path = Path.Combine(Path.GetTempPath(), $"missing-caja-{Guid.NewGuid():N}.json");
        var vm = new CajaRecoveryViewModel(new CajaRecoveryStore(path, Guid.NewGuid(), new Policy()), new FakeSession(), () => generation);
        var reader = new DelayedAccountsReader();
        vm.AttachAccountsReader(reader);
        Assert.True(vm.Open(() => { }));
        var reading = vm.ConsultarCuentasCommand.ExecuteAsync(null);
        generation++;
        reader.Complete();
        await reading;
        Assert.Empty(vm.Cuentas);
        Assert.Contains("No se pudieron consultar", vm.EstadoCuentas, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Opening_requires_review_and_confirmation_and_preserves_uncertainty(bool uncertain)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"caja-opening-{Guid.NewGuid():N}");
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
            var store = new CajaRecoveryStore(path, installation, new Policy());
            var vm = new CajaRecoveryViewModel(store, new FakeSession(), () => 1);
            var transport = new OpeningTransport(vm, uncertain);
            vm.Attach(new CajaRecoveryCoordinator(store, transport, () => 1));
            vm.AttachShiftReader(new EmptyShiftReader());
            Assert.True(vm.Open(() => { }));
            vm.ConfirmarApertura = true;
            await vm.AbrirTurnoCommand.ExecuteAsync(null);
            Assert.Equal(0, transport.Prepares);
            await vm.ConsultarTurnoCommand.ExecuteAsync(null);
            Assert.False(vm.ConfirmarApertura);
            vm.ConfirmarApertura = true;
            await vm.AbrirTurnoCommand.ExecuteAsync(null);
            Assert.Equal(1, transport.Prepares);
            Assert.Equal(uncertain ? 0 : 1, transport.Executions);
            Assert.Equal(uncertain ? "Pendiente" : "SinPendientes", store.Read()!.Estado);
            if (!uncertain) Assert.Contains("Turno 17", vm.ResumenTurno, StringComparison.Ordinal);
            await vm.AbrirTurnoCommand.ExecuteAsync(null);
            Assert.Equal(1, transport.Prepares);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task Recovery_requires_confirmation_and_applies_snapshot_before_clearing_journal()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"caja-view-{Guid.NewGuid():N}");
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
            var store = new CajaRecoveryStore(path, installation, new Policy());
            var operation = Guid.NewGuid();
            Assert.True(store.Begin(operation, 1, "CerrarTurno"));
            var session = new FakeSession();
            var viewModel = new CajaRecoveryViewModel(store, session, () => 1);
            var transport = new FakeTransport(viewModel);
            viewModel.Attach(new CajaRecoveryCoordinator(store, transport, () => 1));
            Assert.True(viewModel.Open(() => { }));
            await viewModel.CancelarCommand.ExecuteAsync(null);
            Assert.Equal(0, transport.Cancellations);
            viewModel.ConfirmarCancelacion = true;
            await viewModel.CancelarCommand.ExecuteAsync(null);
            Assert.Equal(1, transport.Cancellations);
            Assert.Equal("TerminalPorConciliar", store.Read()!.Estado);
            await viewModel.ConciliarCommand.ExecuteAsync(null);
            Assert.Contains("Turno 17", viewModel.Fotografia, StringComparison.Ordinal);
            Assert.Equal("SinPendientes", store.Read()!.Estado);
            Assert.False(viewModel.ConfirmarCancelacion);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    private sealed class Policy : ICajaRecoverySecurityPolicy { public bool IsProtected(string path) => true; }

    private sealed class EmptyShiftReader : ICajaTurnoActualReader
    {
        public Task<(bool Valid, CajaTurnoSnapshot? Snapshot)> ReadCurrentShiftAsync(int actor, CancellationToken cancellationToken) =>
            Task.FromResult<(bool, CajaTurnoSnapshot?)>((true, null));
    }

    private sealed class OpenShiftReader : ICajaTurnoActualReader
    {
        public Task<(bool Valid, CajaTurnoSnapshot? Snapshot)> ReadCurrentShiftAsync(int actor, CancellationToken cancellationToken) =>
            Task.FromResult<(bool, CajaTurnoSnapshot?)>((true, new CajaTurnoSnapshot(17, "Abierto", 0, 0, 0, 0, 0, 0)));
    }

    private sealed class ExternalCollisionTransport(CajaRecoveryViewModel sink) : ICajaRecoveryTransport
    {
        public Action? OnExecute { get; init; }
        public List<(Guid Operation, string? Reference, bool Repeated)> Requests { get; } = [];
        public Task<CajaRecoveryOutcome> PrepareAsync(Guid operation, int actor, string type, object? arguments, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
            Assert.True(json.RootElement.GetProperty("verificacionExterna").GetBoolean());
            Requests.Add((operation, json.RootElement.GetProperty("referencia").GetString(), json.RootElement.GetProperty("referenciaRepetida").GetBoolean()));
            return Task.FromResult(CajaRecoveryOutcome.Prepared);
        }
        public Task<CajaRecoveryOutcome> ExecuteAsync(Guid operation, int actor, CancellationToken cancellationToken)
        {
            OnExecute?.Invoke();
            return Task.FromResult(Requests.Count == 1 ? CajaRecoveryOutcome.Rejected : CajaRecoveryOutcome.Completed);
        }
        public Task<CajaRecoveryOutcome> QueryAsync(Guid operation, int actor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CajaRecoveryOutcome> CancelAsync(Guid operation, int actor, bool confirmaIntervencion, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReconcileAsync(Guid operation, int actor, string type, CancellationToken cancellationToken) => Requests.Count == 1
            ? sink.ApplyPaymentRejectionAsync("REFERENCIA_REQUIERE_CONFIRMACION", new CajaCuentaRecoverySnapshot(3, "Listo", 10, true, null), new CajaTurnoSnapshot(17, "Abierto", 0, 0, 0, 0, 0, 0), cancellationToken)
            : sink.ApplyPaymentAsync(new CajaPagoSnapshot(4, 8, 3, 17, 1, 10, "Tarjeta", [new CajaPagoLineaSnapshot(1, "Comida", 1, 10, 10)]), cancellationToken);
    }

    private sealed class ClosingTransport(ICajaRecoverySnapshotSink sink, bool uncertain) : ICajaRecoveryTransport
    {
        public int Prepares { get; private set; }
        public int Executions { get; private set; }
        public Task<CajaRecoveryOutcome> PrepareAsync(Guid operation, int actor, string type, object? arguments, CancellationToken cancellationToken)
        {
            Prepares++;
            Assert.Equal("CerrarTurno", type);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
            Assert.Equal(17, json.RootElement.GetProperty("turnoId").GetInt32());
            Assert.Equal(5m, json.RootElement.GetProperty("efectivoContado").GetDecimal());
            Assert.Equal("Conteo ficticio", json.RootElement.GetProperty("motivo").GetString());
            Assert.True(json.RootElement.GetProperty("confirmado").GetBoolean());
            return Task.FromResult(CajaRecoveryOutcome.Prepared);
        }
        public Task<CajaRecoveryOutcome> ExecuteAsync(Guid operation, int actor, CancellationToken cancellationToken)
        {
            Executions++;
            return Task.FromResult(uncertain ? CajaRecoveryOutcome.Uncertain : CajaRecoveryOutcome.Completed);
        }
        public Task<CajaRecoveryOutcome> QueryAsync(Guid operation, int actor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CajaRecoveryOutcome> CancelAsync(Guid operation, int actor, bool confirmaIntervencion, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReconcileAsync(Guid operation, int actor, string type, CancellationToken cancellationToken) =>
            sink.ApplyShiftAsync(new CajaTurnoSnapshot(17, "Cerrado", 0, 0, 0, 0, 0, 0), cancellationToken);
    }

    private sealed class CashAccountsReader : ICajaCuentasReader
    {
        public Task<CajaCuentasPaginaVista?> ReadAsync(int actor, string? cursor, CancellationToken cancellationToken) =>
            Task.FromResult<CajaCuentasPaginaVista?>(new CajaCuentasPaginaVista(
                [new CajaCuentaVista(3, "Mesa 01", "Listo", 10, true, "Cobrable", [new CajaCuentaLineaVista("Comida")], new string('a', 64)),
                 new CajaCuentaVista(4, "Mesa 02", "Abierto", 10, false, "Comandas pendientes", [], null)], null));
    }

    private sealed class CashTransport(ICajaRecoverySnapshotSink sink, bool uncertain, bool reconciliationFails) : ICajaRecoveryTransport
    {
        public int Prepares { get; private set; }
        public int Executions { get; private set; }
        public Task<CajaRecoveryOutcome> PrepareAsync(Guid operation, int actor, string type, object? arguments, CancellationToken cancellationToken)
        {
            Prepares++;
            Assert.Equal("CobrarCuenta", type);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
            Assert.Equal(3, json.RootElement.GetProperty("pedidoId").GetInt32());
            Assert.Equal(17, json.RootElement.GetProperty("turnoId").GetInt32());
            Assert.Equal(15m, json.RootElement.GetProperty("efectivoRecibido").GetDecimal());
            return Task.FromResult(CajaRecoveryOutcome.Prepared);
        }
        public Task<CajaRecoveryOutcome> ExecuteAsync(Guid operation, int actor, CancellationToken cancellationToken)
        {
            Executions++;
            return Task.FromResult(uncertain ? CajaRecoveryOutcome.Uncertain : CajaRecoveryOutcome.Completed);
        }
        public Task<CajaRecoveryOutcome> QueryAsync(Guid operation, int actor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CajaRecoveryOutcome> CancelAsync(Guid operation, int actor, bool confirmaIntervencion, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReconcileAsync(Guid operation, int actor, string type, CancellationToken cancellationToken) =>
            reconciliationFails ? Task.FromResult(false) : sink.ApplyPaymentAsync(new CajaPagoSnapshot(4, 8, 3, 17, 1, 10, "Efectivo", [new CajaPagoLineaSnapshot(1, "Comida", 1, 10, 10)], 15, 5), cancellationToken);
    }

    private sealed class DelayedAccountsReader : ICajaCuentasReader
    {
        private readonly TaskCompletionSource<CajaCuentasPaginaVista?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CajaCuentasPaginaVista?> ReadAsync(int actor, string? cursor, CancellationToken cancellationToken) => _completion.Task;
        public void Complete() => _completion.SetResult(new CajaCuentasPaginaVista(
            [new CajaCuentaVista(1, "Mesa 01", "Listo", 10, true, "Cobrable", [])], null));
    }

    private sealed class OpeningTransport(ICajaRecoverySnapshotSink sink, bool uncertain) : ICajaRecoveryTransport
    {
        public int Prepares { get; private set; }
        public int Executions { get; private set; }
        public Task<CajaRecoveryOutcome> PrepareAsync(Guid operation, int actor, string type, object? arguments, CancellationToken cancellationToken)
        {
            Prepares++;
            Assert.Equal("AbrirTurno", type);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
            Assert.Equal(0m, json.RootElement.GetProperty("fondoInicial").GetDecimal());
            Assert.True(json.RootElement.GetProperty("confirmado").GetBoolean());
            return Task.FromResult(uncertain ? CajaRecoveryOutcome.Uncertain : CajaRecoveryOutcome.Prepared);
        }
        public Task<CajaRecoveryOutcome> ExecuteAsync(Guid operation, int actor, CancellationToken cancellationToken)
        {
            Executions++;
            return Task.FromResult(CajaRecoveryOutcome.Completed);
        }
        public Task<CajaRecoveryOutcome> QueryAsync(Guid operation, int actor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CajaRecoveryOutcome> CancelAsync(Guid operation, int actor, bool confirmaIntervencion, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReconcileAsync(Guid operation, int actor, string type, CancellationToken cancellationToken) =>
            sink.ApplyShiftAsync(new CajaTurnoSnapshot(17, "Abierto", 0, 0, 0, 0, 0, 0), cancellationToken);
    }

    private sealed class FakeTransport(ICajaRecoverySnapshotSink sink) : ICajaRecoveryTransport
    {
        public int Cancellations { get; private set; }
        public Task<CajaRecoveryOutcome> PrepareAsync(Guid operation, int actor, string type, object? arguments, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CajaRecoveryOutcome> ExecuteAsync(Guid operation, int actor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CajaRecoveryOutcome> QueryAsync(Guid operation, int actor, CancellationToken cancellationToken) => Task.FromResult(CajaRecoveryOutcome.Prepared);
        public Task<CajaRecoveryOutcome> CancelAsync(Guid operation, int actor, bool confirmaIntervencion, CancellationToken cancellationToken)
        {
            Cancellations++;
            return Task.FromResult(CajaRecoveryOutcome.Cancelled);
        }
        public Task<bool> ReconcileAsync(Guid operation, int actor, string type, CancellationToken cancellationToken) =>
            sink.ApplyShiftAsync(new CajaTurnoSnapshot(17, "Abierto", 10, 0, 0, 0, 10, 0), cancellationToken);
    }

    private sealed class FakeSession : ISessionManager
    {
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged { add { } remove { } }
        public bool HasSession => true;
        public SessionLifecycleState State => SessionLifecycleState.Active;
        public SessionSnapshot? CurrentSession => new(1, "jefa", "Jefa", DateTimeOffset.MaxValue, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue);
        public Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EvaluateState() { }
    }
}
