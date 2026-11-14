using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;
using Microsoft.EntityFrameworkCore;

// Acceptance host only: the journal policy and session adapter do not claim Windows ACL or UI validation.
internal static class CajaSmoke
{
    public static async Task RunAsync(HttpClient client, TemporaryPostgresDatabase database, CancellationToken token)
    {
        var orderIds = new List<int>();
        await using (var setup = database.CreateContext())
        {
            var product = new Producto { Nombre = "Producto ficticio", Categoria = "Comida", Precio = 10m, Disponible = true };
            setup.Productos.Add(product);
            await setup.SaveChangesAsync(token);
            foreach (var number in new[] { 4, 5, 6, 7, 8 })
            {
                var table = await setup.Mesas.SingleAsync(x => x.Numero == number, token);
                table.Estado = MesaStates.Ocupada;
                var order = new Pedido { IdMesa = table.IdMesa, IdUsuario = 1, FechaHora = DateTimeOffset.UtcNow, Estado = PedidoStates.Listo, Subtotal = 10m, Total = 10m };
                setup.Pedidos.Add(order);
                await setup.SaveChangesAsync(token);
                orderIds.Add(order.IdPedido);
                var command = new Comanda { IdPedido = order.IdPedido, IdUsuario = 1, FechaHora = DateTimeOffset.UtcNow, Estado = "Lista" };
                command.Detalles.Add(new DetallePedido { IdProducto = product.IdProducto, NombreProducto = product.Nombre, Cantidad = 1, PrecioUnitario = 10m, Subtotal = 10m, Notas = "" });
                setup.Comandas.Add(command);
                await setup.SaveChangesAsync(token);
            }
        }

        client.DefaultRequestHeaders.Authorization = null;
        using var login = await client.PostAsJsonAsync("api/auth/login", new { nombre = "jefa central", contrasena = "correct horse battery staple" }, token);
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthSessionResponse>(cancellationToken: token)
            ?? throw new InvalidDataException("No se recibió la sesión temporal.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var session = new LoopbackSession(client, auth);
        var tempRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode");
        if (!Directory.Exists(tempRoot)) tempRoot = Path.GetTempPath();
        var directory = Path.Combine(tempRoot, $"caja-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "journal.json");
        var installation = Guid.NewGuid();
        try
        {
            await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1,
                instalacion = installation,
                estado = "SinPendientes",
                operacion = (Guid?)null,
                propietario = (int?)null,
                tipo = (string?)null
            }), token);
            await File.WriteAllTextAsync(path + ".lock", "", token);
            var store = new CajaRecoveryStore(path, installation, new TestOnlyPolicy());
            var vm = new CajaRecoveryViewModel(store, session, () => 1);
            var transport = new CajaRecoveryHttpTransport(session, vm);
            var coordinator = new CajaRecoveryCoordinator(store, transport, () => session.CurrentSession?.IdUsuario);
            vm.Attach(coordinator);
            vm.AttachShiftReader(transport);
            vm.AttachAccountsReader(new CajaCuentasReader(session));
            vm.AttachReceiptsReader(transport);
            Require(vm.Open(() => { }), "No se abrió el ViewModel de caja.");
            var missingOperation = Guid.NewGuid();
            Require(store.Begin(missingOperation, auth.IdUsuario, "AbrirTurno"), "No se pudo registrar la identidad de prueba.");
            Require(await coordinator.RecoverAsync(token) == CajaRecoveryOutcome.NotFound, "La operación inexistente no se identificó.");
            Require(!await coordinator.ReconcileAsync(token) && store.Read()?.Operacion == missingOperation, "No encontrada desbloqueó el diario.");
            Require(await coordinator.BeginAsync(Guid.NewGuid(), auth.IdUsuario, "AbrirTurno", token, new { fondoInicial = 0m, confirmado = true }) == CajaRecoveryOutcome.Uncertain, "Se permitió otra intención con diario pendiente.");
            Require(await coordinator.CancelAsync(token) == CajaRecoveryOutcome.Cancelled, "No se creó la barrera de cancelación.");
            await vm.ConciliarCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "SinPendientes", "La barrera de cancelación no se concilió.");
            var preparedOperation = Guid.NewGuid();
            Require(await coordinator.BeginAsync(preparedOperation, auth.IdUsuario, "AbrirTurno", token, new { fondoInicial = 0m, confirmado = true }) == CajaRecoveryOutcome.Prepared, "No se preparó la apertura de prueba.");
            Require(await coordinator.RecoverAsync(token) == CajaRecoveryOutcome.Prepared && !await coordinator.ReconcileAsync(token), "Preparada desbloqueó el diario.");
            Require(await coordinator.ExecuteAsync(token) == CajaRecoveryOutcome.Uncertain && store.Read()?.Operacion == preparedOperation, "La consulta reactivó una ejecución preparada.");
            Require(await coordinator.CancelAsync(token) == CajaRecoveryOutcome.Cancelled, "No se canceló la preparación.");
            await vm.ConciliarCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "SinPendientes", "No se concilió la preparación cancelada.");
            await vm.ConsultarTurnoCommand.ExecuteAsync(null);
            vm.ConfirmarApertura = true;
            await vm.AbrirTurnoCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "SinPendientes" && vm.Mensaje.Contains("abierto y reconciliado", StringComparison.Ordinal), "La apertura no quedó reconciliada.");
            await vm.ConsultarCuentasCommand.ExecuteAsync(null);
            var account = vm.Cuentas.SingleOrDefault(x => x.IdPedido == orderIds[0]);
            Require(account is { Cobrable: true }, "La cuenta ficticia no se mostró cobrable.");
            vm.SeleccionarCuentaCommand.Execute(account);
            Require(vm.CuentaSeleccionada is not null, "No se seleccionó la cuenta ficticia.");
            vm.EfectivoRecibido = "15.00";
            vm.ConfirmarCobro = true;
            await vm.CobrarEfectivoCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "SinPendientes", "El diario no se concilió.");
            Require(vm.Mensaje.Contains("Cobro confirmado", StringComparison.Ordinal) && vm.Fotografia.Contains("vuelto", StringComparison.OrdinalIgnoreCase), "No se aplicó el comprobante de efectivo.");
            await vm.ConsultarTurnoCommand.ExecuteAsync(null);
            await vm.ConsultarCuentasCommand.ExecuteAsync(null);
            var second = vm.Cuentas.SingleOrDefault(x => x.IdPedido == orderIds[1]);
            Require(second is { Cobrable: true }, "La segunda cuenta ficticia no se mostró cobrable.");
            vm.SeleccionarCuentaCommand.Execute(second);
            vm.EfectivoRecibido = "15.00";
            vm.ConfirmarCobro = true;
            session.DropNextExecutionResponse = true;
            var executionsBefore = session.Executions;
            await vm.CobrarEfectivoCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "Pendiente" && session.Executions == executionsBefore + 1, "No se conservó la operación incierta.");
            var uncertainOperation = store.Read()?.Operacion ?? throw new InvalidDataException("Falta la operación incierta.");
            await vm.ConsultarTurnoCommand.ExecuteAsync(null);
            await vm.ConsultarCuentasCommand.ExecuteAsync(null);
            Require(store.Read()?.Operacion == uncertainOperation && store.Read()?.Estado == "Pendiente", "Una consulta de negocio desbloqueó el diario.");
            Require(await coordinator.BeginAsync(Guid.NewGuid(), auth.IdUsuario, "AbrirTurno", token, new { fondoInicial = 0m, confirmado = true }) == CajaRecoveryOutcome.Uncertain, "Una consulta permitió otra intención mientras existía incertidumbre.");
            using (var cancellation = await client.PostAsJsonAsync($"api/caja/operaciones/{uncertainOperation:D}/cancelacion", new { confirmaIntervencion = false }, token))
            {
                cancellation.EnsureSuccessStatusCode();
                using var result = await JsonDocument.ParseAsync(await cancellation.Content.ReadAsStreamAsync(token), cancellationToken: token);
                Require(result.RootElement.GetProperty("estado").GetString() == "Completada", "La cancelación no devolvió el cobro ya confirmado.");
            }
            Require(store.Read()?.Estado == "Pendiente", "La cancelación fuera del coordinador desbloqueó el diario.");
            // Simulate a Desktop restart: reconstruct the store, coordinator, transport and view model from the same journal.
            vm.Deactivate();
            store = new CajaRecoveryStore(path, installation, new TestOnlyPolicy());
            vm = new CajaRecoveryViewModel(store, session, () => 1);
            transport = new CajaRecoveryHttpTransport(session, vm);
            coordinator = new CajaRecoveryCoordinator(store, transport, () => session.CurrentSession?.IdUsuario);
            vm.Attach(coordinator);
            vm.AttachShiftReader(transport);
            vm.AttachAccountsReader(new CajaCuentasReader(session));
            vm.AttachReceiptsReader(transport);
            Require(vm.Open(() => { }) && store.Read()?.Operacion == uncertainOperation, "El reinicio perdió la operación incierta.");
            await vm.ConsultarCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "TerminalPorConciliar", "La consulta no recuperó el resultado durable.");
            await vm.ConciliarCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "SinPendientes" && session.Executions == executionsBefore + 1, "La recuperación repitió la ejecución o no concilió.");
            async Task SelectExternal(int id, string method, string reference)
            {
                await vm.ConsultarTurnoCommand.ExecuteAsync(null);
                await vm.ConsultarCuentasCommand.ExecuteAsync(null);
                var selected = vm.Cuentas.SingleOrDefault(x => x.IdPedido == id);
                Require(selected is { Cobrable: true }, "La cuenta externa no está disponible.");
                vm.SeleccionarCuentaCommand.Execute(selected);
                vm.MetodoPago = method;
                vm.ReferenciaPago = reference;
                vm.ConfirmarCobro = true;
                vm.ConfirmarVerificacionExterna = true;
            }
            await SelectExternal(orderIds[2], "Tarjeta", "REF-DEMO-CARD");
            await vm.CobrarExternoCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "SinPendientes" && vm.Mensaje.Contains("registrado", StringComparison.Ordinal), "No se registró tarjeta.");
            await SelectExternal(orderIds[3], "Transferencia", "REF-DEMO-TRANSFER");
            var externalExecutions = session.Executions;
            session.DropNextExecutionResponse = true;
            await vm.CobrarExternoCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "Pendiente" && session.Executions == externalExecutions + 1, "La transferencia incierta no conservó el diario.");
            await vm.ConsultarCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "TerminalPorConciliar", "No se recuperó la transferencia confirmada.");
            await vm.ConciliarCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "SinPendientes" && session.Executions == externalExecutions + 1 && vm.Fotografia.Contains("Transferencia", StringComparison.Ordinal), "La transferencia no se concilió sin repetir.");
            await SelectExternal(orderIds[4], "Tarjeta", "REF-DEMO-CARD");
            await vm.CobrarExternoCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "TerminalPorConciliar", "La referencia repetida no quedó rechazada y bloqueada.");
            await vm.ConciliarCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "SinPendientes" && vm.Mensaje.Contains("coincide", StringComparison.Ordinal), "No se concilió la advertencia de referencia.");
            await SelectExternal(orderIds[4], "Tarjeta", "REF-DEMO-CARD");
            await vm.CobrarExternoCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "SinPendientes" && vm.Mensaje.Contains("confirme", StringComparison.OrdinalIgnoreCase), "Se omitió la segunda confirmación.");
            vm.ConfirmarReferenciaRepetida = true;
            await vm.CobrarExternoCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "SinPendientes" && vm.Mensaje.Contains("registrado", StringComparison.Ordinal), "No se registró la referencia repetida revisada.");
            await vm.ConsultarComprobantesCommand.ExecuteAsync(null);
            Require(vm.Comprobantes.Count == 5, "El historial no mostró los cinco comprobantes.");
            await vm.VerComprobanteCommand.ExecuteAsync(vm.Comprobantes[0]);
            Require(vm.Fotografia.Contains("NO ES FACTURA FISCAL", StringComparison.Ordinal) && vm.Fotografia.Contains("Producto ficticio", StringComparison.Ordinal), "El detalle histórico no se aplicó.");
            await using var verify = database.CreateContext();
            var payment = await verify.Cobros.SingleAsync(x => x.IdPedido == orderIds[0], token);
            Require(payment.Total == 10m && payment.EfectivoRecibido == 15m && payment.Vuelto == 5m, "El cobro persistido no coincide.");
            foreach (var orderId in orderIds)
            {
                Require(await verify.Pedidos.Where(x => x.IdPedido == orderId).Select(x => x.Estado).SingleAsync(token) == PedidoStates.Pagado, "La cuenta no quedó pagada.");
                Require(await verify.Cobros.CountAsync(x => x.IdPedido == orderId, token) == 1, "Se registró más de un cobro.");
            }
            await vm.ConsultarTurnoCommand.ExecuteAsync(null);
            vm.EfectivoContado = "20.00";
            Require(vm.DiferenciaCierre.Contains("Sin diferencia", StringComparison.Ordinal) && vm.DiferenciaCierre.Contains("pendientes: 2", StringComparison.OrdinalIgnoreCase), "El resumen de cierre no reflejó las cuentas pendientes.");
            vm.ConfirmarCierreCaja = true;
            await vm.CerrarTurnoCommand.ExecuteAsync(null);
            Require(store.Read()?.Estado == "SinPendientes" && vm.Mensaje.Contains("cerrado y reconciliado", StringComparison.Ordinal), "El cierre no quedó reconciliado.");
            var shift = await verify.TurnosCaja.AsNoTracking().SingleAsync(token);
            Require(shift.Estado == CajaStates.TurnoCerrado && shift.EfectivoEsperado == 20m && shift.EfectivoContado == 20m && shift.TotalTarjeta == 20m && shift.TotalTransferencia == 10m && shift.CantidadCuentasPendientes == 2, "El cierre persistido no coincide.");
            Console.WriteLine("CAJA SMOKE: barreras, preparación, consultas, cobro confirmado no reversible, recuperación y cierre con datos ficticios.");
            Console.WriteLine("Sin ventana ni render; sesión adaptada y política del diario solo de prueba, no valida ACL ni LAN.");
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".lock");
            Directory.Delete(directory);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private sealed class TestOnlyPolicy : ICajaRecoverySecurityPolicy { public bool IsProtected(string path) => true; }

    private sealed class LoopbackSession(HttpClient client, AuthSessionResponse auth) : ISessionManager, ICajaSessionSender
    {
        public bool DropNextExecutionResponse { get; set; }
        public int Executions { get; private set; }
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged { add { } remove { } }
        public bool HasSession => true;
        public SessionLifecycleState State => SessionLifecycleState.Active;
        public SessionSnapshot? CurrentSession => new(auth.IdUsuario, auth.Nombre, auth.Rol, auth.AccessTokenExpiresAt, auth.InactivityExpiresAt, auth.AbsoluteSessionExpiresAt);
        public async Task<HttpResponseMessage> SendCajaOnceAsync(int expectedActor, Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default)
        {
            if (expectedActor != auth.IdUsuario) throw new InvalidOperationException("Actor inesperado.");
            using var request = requestFactory();
            var execution = request.RequestUri!.ToString().EndsWith("/ejecucion", StringComparison.Ordinal);
            if (execution) Executions++;
            var response = await client.SendAsync(request, cancellationToken);
            if (execution && DropNextExecutionResponse)
            {
                DropNextExecutionResponse = false;
                if (!response.IsSuccessStatusCode) return response;
                response.Dispose();
                throw new HttpRequestException("Pérdida simulada de respuesta después de confirmación HTTP.");
            }
            return response;
        }
        public Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EvaluateState() { }
    }
}
