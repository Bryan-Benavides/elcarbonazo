using System.Net;
using System.Net.Http.Json;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class InventarioEscrituraCoordinatorTests
{
    [Theory]
    [InlineData(500)]
    [InlineData(401)]
    public async Task Uncertain_write_is_not_repeated_or_unlocked_by_later_calls(int status)
    {
        var session = new SessionFake();
        var sender = new SenderFake { Response = () => new HttpResponseMessage((HttpStatusCode)status) };
        var coordinator = new InventarioEscrituraCoordinator(session, sender);
        Assert.Equal(InventarioEscrituraEstado.Incierta, (await coordinator.EnviarAsync(Request)).Estado);
        Assert.Equal(InventarioEscrituraEstado.Bloqueada, (await coordinator.EnviarAsync(Request)).Estado);
        Assert.True(coordinator.ResultadoIncierto);
        Assert.Equal(1, sender.Calls);
    }

    [Fact]
    public async Task Malformed_success_and_session_change_keep_blocked()
    {
        var session = new SessionFake();
        var sender = new SenderFake { Response = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { }) } };
        var coordinator = new InventarioEscrituraCoordinator(session, sender);
        Assert.Equal(InventarioEscrituraEstado.Incierta, (await coordinator.EnviarAsync(Request)).Estado);
        Assert.True(coordinator.ResultadoIncierto);

        sender = new SenderFake { Response = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = 2 }) }, ChangeGeneration = true };
        coordinator = new InventarioEscrituraCoordinator(session, sender);
        Assert.Equal(InventarioEscrituraEstado.Incierta, (await coordinator.EnviarAsync(Request)).Estado);
        Assert.True(coordinator.ResultadoIncierto);
    }

    [Fact]
    public async Task Valid_conflict_is_terminal_but_concurrent_writes_are_not_started()
    {
        var session = new SessionFake();
        var sender = new SenderFake
        {
            Response = () => new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new ApiProblem(409, "VERSION_CONFLICT", null, null, null))
            }
        };
        var coordinator = new InventarioEscrituraCoordinator(session, sender);
        var result = await coordinator.EnviarAsync(Request);
        Assert.Equal(InventarioEscrituraEstado.Rechazada, result.Estado);
        Assert.Equal("VERSION_CONFLICT", result.Codigo);
        Assert.False(coordinator.ResultadoIncierto);
        sender.Block = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = coordinator.EnviarAsync(Request);
        Assert.Equal(InventarioEscrituraEstado.Bloqueada, (await coordinator.EnviarAsync(Request)).Estado);
        sender.Block.SetResult();
        Assert.Equal(InventarioEscrituraEstado.Rechazada, (await first).Estado);
        Assert.Equal(2, sender.Calls);
    }

    [Fact]
    public async Task Pause_and_resume_during_write_keeps_uncertainty_even_if_response_is_success()
    {
        var session = new SessionFake();
        var sender = new SenderFake
        {
            Response = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = 1 }) },
            Block = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        using var coordinator = new InventarioEscrituraCoordinator(session, sender);
        var pending = coordinator.EnviarAsync(Request);
        session.ChangeState(SessionLifecycleState.Paused);
        session.ChangeState(SessionLifecycleState.Active);
        sender.Block.SetResult();
        Assert.Equal(InventarioEscrituraEstado.Incierta, (await pending).Estado);
        Assert.True(coordinator.ResultadoIncierto);
    }

    [Fact]
    public async Task Closing_and_reopening_view_keeps_uncertain_write_blocked()
    {
        var session = new SessionFake();
        var sender = new SenderFake { Response = () => new HttpResponseMessage(HttpStatusCode.InternalServerError) };
        using var coordinator = new InventarioEscrituraCoordinator(session, sender);
        using (var first = new InventarioLecturaViewModel(new ReaderFake(), session, true, () => { }, coordinator))
        {
            first.NombreNuevo = "Arroz";
            first.ConfirmarIngrediente = true;
            await first.CrearIngredienteCommand.ExecuteAsync(null);
            Assert.Contains("incierto", first.EstadoEscritura, StringComparison.OrdinalIgnoreCase);
        }
        using var second = new InventarioLecturaViewModel(new ReaderFake(), session, true, () => { }, coordinator);
        Assert.Contains("incierta", second.EstadoEscritura, StringComparison.OrdinalIgnoreCase);
        second.NombreNuevo = "Otro";
        second.ConfirmarIngrediente = true;
        await second.CrearIngredienteCommand.ExecuteAsync(null);
        Assert.Equal(1, sender.Calls);
    }

    [Fact]
    public async Task Invalid_quantity_is_not_sent_and_confirmation_is_cleared()
    {
        var session = new SessionFake();
        var sender = new SenderFake();
        using var coordinator = new InventarioEscrituraCoordinator(session, sender);
        using var view = new InventarioLecturaViewModel(new ReaderFake(), session, true, () => { }, coordinator);
        view.NombreNuevo = "Arroz";
        view.MinimoNuevo = "0.00001";
        view.ConfirmarIngrediente = true;
        await view.CrearIngredienteCommand.ExecuteAsync(null);
        Assert.Equal(0, sender.Calls);
        Assert.False(view.ConfirmarIngrediente);
    }

    [Fact]
    public async Task Movement_requires_fresh_selection_and_explicit_confirmation()
    {
        var session = new SessionFake();
        var sender = new SenderFake { Response = () => new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new { id = 1 }) } };
        using var coordinator = new InventarioEscrituraCoordinator(session, sender);
        using var view = new InventarioLecturaViewModel(new ReaderFake(), session, true, () => { }, coordinator);
        var ingredient = new IngredienteLectura(1, "Arroz", "lb", 2, 0, true, "Normal");
        view.IngredienteSeleccionado = ingredient;
        view.TipoMovimiento = "AjusteNegativo";
        view.CantidadMovimiento = "0.5";
        view.MotivoMovimiento = "Merma";
        view.ConfirmarMovimiento = true;
        await view.RegistrarMovimientoCommand.ExecuteAsync(null);
        Assert.Equal(0, sender.Calls);
        await view.ConsultarIngredientesCommand.ExecuteAsync(null);
        view.IngredienteSeleccionado = Assert.Single(view.Ingredientes);
        view.ConfirmarMovimiento = true;
        await view.RegistrarMovimientoCommand.ExecuteAsync(null);
        Assert.Equal(1, sender.Calls);
        Assert.Contains("confirmada", view.EstadoEscritura, StringComparison.OrdinalIgnoreCase);
        view.ConfirmarMovimiento = true;
        await view.RegistrarMovimientoCommand.ExecuteAsync(null);
        Assert.Equal(1, sender.Calls);
    }

    [Fact]
    public async Task Editing_requires_versioned_fresh_selection_and_explicit_confirmation()
    {
        var session = new SessionFake();
        var sender = new SenderFake { Response = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = 1 }) } };
        using var coordinator = new InventarioEscrituraCoordinator(session, sender);
        using var view = new InventarioLecturaViewModel(new ReaderFake(), session, true, () => { }, coordinator);
        await view.ConsultarIngredientesCommand.ExecuteAsync(null);
        view.IngredienteSeleccionado = Assert.Single(view.Ingredientes);
        view.NombreEdicion = "Arroz nuevo";
        await view.ActualizarIngredienteCommand.ExecuteAsync(null);
        Assert.Equal(0, sender.Calls);
        view.ConfirmarEdicion = true;
        await view.ActualizarIngredienteCommand.ExecuteAsync(null);
        Assert.Equal(1, sender.Calls);
        Assert.Contains("confirmada", view.EstadoEscritura, StringComparison.OrdinalIgnoreCase);
        view.ConfirmarEdicion = true;
        await view.ActualizarIngredienteCommand.ExecuteAsync(null);
        Assert.Equal(1, sender.Calls);
    }

    [Fact]
    public async Task Recipe_editor_requires_fresh_reads_rejects_duplicates_and_sends_version()
    {
        var session = new SessionFake();
        var sender = new SenderFake { Response = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = 1 }) } };
        using var coordinator = new InventarioEscrituraCoordinator(session, sender);
        using var view = new InventarioLecturaViewModel(new ReaderFake(), session, true, () => { }, coordinator);
        await view.ConsultarIngredientesCommand.ExecuteAsync(null);
        view.IngredienteSeleccionado = Assert.Single(view.Ingredientes);
        await view.ConsultarProductosCommand.ExecuteAsync(null);
        view.ProductoSeleccionado = Assert.Single(view.Productos);
        await view.ConsultarRecetaCommand.ExecuteAsync(null);
        view.CantidadReceta = "0.25";
        view.AgregarLineaRecetaCommand.Execute(null);
        view.CantidadReceta = "0.5";
        view.AgregarLineaRecetaCommand.Execute(null);
        Assert.Single(view.LineasReceta);
        view.RecetaActiva = true;
        view.ConfirmarReceta = true;
        await view.GuardarRecetaCommand.ExecuteAsync(null);
        Assert.Equal(1, sender.Calls);
        Assert.Contains("\"versionEsperada\":\"2\"", sender.Body, StringComparison.Ordinal);
        Assert.Contains("\"cantidadRequerida\":0.25", sender.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_recipe_refresh_invalidates_previous_review_without_sending()
    {
        var session = new SessionFake();
        var sender = new SenderFake();
        var reader = new ReaderFake();
        using var coordinator = new InventarioEscrituraCoordinator(session, sender);
        using var view = new InventarioLecturaViewModel(reader, session, true, () => { }, coordinator);
        await view.ConsultarProductosCommand.ExecuteAsync(null);
        view.ProductoSeleccionado = Assert.Single(view.Productos);
        await view.ConsultarRecetaCommand.ExecuteAsync(null);
        reader.FailRecipe = true;
        await view.ConsultarRecetaCommand.ExecuteAsync(null);
        view.ConfirmarReceta = true;
        await view.GuardarRecetaCommand.ExecuteAsync(null);
        Assert.Equal(0, sender.Calls);
        Assert.Contains("no se pudo", view.DetalleReceta, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stale_ingredient_rejection_requires_fresh_review_and_is_not_retried()
    {
        var session = new SessionFake();
        var sender = new SenderFake
        {
            Response = () => new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new ApiProblem(409, "VERSION_CONFLICT", null, null, null))
            }
        };
        using var coordinator = new InventarioEscrituraCoordinator(session, sender);
        using var view = new InventarioLecturaViewModel(new ReaderFake(), session, true, () => { }, coordinator);
        await view.ConsultarIngredientesCommand.ExecuteAsync(null);
        view.IngredienteSeleccionado = Assert.Single(view.Ingredientes);
        view.ConfirmarEdicion = true;
        await view.ActualizarIngredienteCommand.ExecuteAsync(null);
        Assert.Contains("VERSION_CONFLICT", view.EstadoEscritura, StringComparison.Ordinal);
        view.ConfirmarEdicion = true;
        await view.ActualizarIngredienteCommand.ExecuteAsync(null);
        Assert.Equal(1, sender.Calls);
        Assert.False(coordinator.ResultadoIncierto);
    }

    private sealed class ReaderFake : IInventarioLecturaService
    {
        public bool FailRecipe { get; set; }
        public Task<InventarioPaginaLectura<IngredienteLectura>> IngredientesAsync(string? cursor, CancellationToken ct) =>
            Task.FromResult(new InventarioPaginaLectura<IngredienteLectura>([new(1, "Arroz", "lb", 2, 0, true, "Normal", "3")], null));
        public Task<InventarioPaginaLectura<ProductoRecetaLectura>> ProductosAsync(string? cursor, CancellationToken ct) =>
            Task.FromResult(new InventarioPaginaLectura<ProductoRecetaLectura>([new(1, "Plato", "Activa")], null));
        public Task<InventarioPaginaLectura<ProductoRecetaLectura>> ProductosFiltradosAsync(string? cursor, string? busqueda, CancellationToken ct) => ProductosAsync(cursor, ct);
        public Task<string> RecetaAsync(int productoId, CancellationToken ct) => throw new NotSupportedException();
        public Task<ResumenAlertasLectura> AlertasAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<InventarioPaginaLectura<IngredienteLectura>> IngredientesFiltradosAsync(string? cursor, string? estado, string? busqueda, CancellationToken ct) => throw new NotSupportedException();
        public Task<InventarioPaginaLectura<MovimientoLectura>> MovimientosAsync(int ingredientId, string? cursor, CancellationToken ct) => throw new NotSupportedException();
        public Task<RecetaLectura> DetalleRecetaAsync(int productoId, CancellationToken ct) =>
            FailRecipe ? Task.FromException<RecetaLectura>(new InvalidDataException()) :
            Task.FromResult(new RecetaLectura(productoId, true, false, "2", null, []));
    }

    private static HttpRequestMessage Request() => new(HttpMethod.Post, "api/inventario/movimientos");

    private sealed class SenderFake : IInventarioSessionSender
    {
        public long InventarioSessionGeneration { get; private set; } = 1;
        public int Calls { get; private set; }
        public bool ChangeGeneration { get; init; }
        public string? Body { get; private set; }
        public Func<HttpResponseMessage> Response { get; init; } = () => new(HttpStatusCode.OK);
        public TaskCompletionSource? Block { get; set; }
        public async Task<HttpResponseMessage> SendInventarioOnceAsync(int expectedActor, long expectedGeneration,
            Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default)
        {
            Calls++;
            using var request = requestFactory();
            if (request.Content is not null) Body = await request.Content.ReadAsStringAsync(cancellationToken);
            if (Block is not null) await Block.Task;
            if (ChangeGeneration) InventarioSessionGeneration++;
            return Response();
        }
    }

    private sealed class SessionFake : ISessionManager
    {
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged;
        public bool HasSession => true;
        public SessionLifecycleState State { get; private set; } = SessionLifecycleState.Active;
        public void ChangeState(SessionLifecycleState state)
        {
            var previous = State;
            State = state;
            SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs(previous, state));
        }
        public SessionSnapshot? CurrentSession => new(1, "Jefa", "Jefa", DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow.AddMinutes(15), DateTimeOffset.UtcNow.AddHours(1));
        public Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EvaluateState() { }
    }
}
