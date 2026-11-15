using System.Net;
using System.Text;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;
using Xunit;

namespace ElCarbonazo.Desktop.Tests;

public sealed class InventarioLecturaTests
{
    [Fact]
    public async Task Reader_validates_inventory_and_uses_read_only_session()
    {
        var session = new FakeSession
        {
            Json = """{"items":[{"idIngrediente":1,"nombre":"Carne","unidadMedida":"lb","stockActual":0.5,"stockMinimo":1,"activo":true,"estadoAlerta":"StockBajo","version":"1"}],"cursorSiguiente":null}"""
        };
        var page = await new InventarioLecturaService(session).IngredientesAsync(null, default);
        Assert.Equal("StockBajo", Assert.Single(page.Items).Estado);
        Assert.Equal(1, session.Reads);
        Assert.Equal(0, session.Writes);
        session.Json = """{"items":[{"idIngrediente":1,"nombre":"Carne","unidadMedida":"lb","stockActual":0,"stockMinimo":1,"activo":true,"estadoAlerta":"Normal","version":"1"}],"cursorSiguiente":null}""";
        await Assert.ThrowsAsync<InvalidDataException>(() => new InventarioLecturaService(session).IngredientesAsync(null, default));
    }

    [Fact]
    public async Task Alert_summary_rejects_inconsistent_counts()
    {
        var session = new FakeSession { Json = """{"totalActivos":2,"stockBajo":1,"agotados":1,"pagina":{"items":[],"cursorSiguiente":null}}""" };
        var reader = new InventarioLecturaService(session);
        var summary = await reader.AlertasAsync(default);
        Assert.Equal(2, summary.TotalActivos);
        session.Json = """{"totalActivos":1,"stockBajo":1,"agotados":1,"pagina":{"items":[],"cursorSiguiente":null}}""";
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.AlertasAsync(default));
    }

    [Fact]
    public async Task Filtered_inventory_and_ledger_validate_server_responses()
    {
        var session = new FakeSession
        {
            Json = """{"items":[{"idIngrediente":1,"nombre":"Arroz","unidadMedida":"lb","stockActual":0,"stockMinimo":1,"activo":true,"estadoAlerta":"Agotado","version":"2"}],"cursorSiguiente":null}"""
        };
        var reader = new InventarioLecturaService(session);
        var page = await reader.IngredientesFiltradosAsync(null, "Agotado", "arroz", default);
        Assert.Equal("2", Assert.Single(page.Items).Version);
        Assert.Contains("estadoAlerta=Agotado", session.Path, StringComparison.Ordinal);
        session.Json = """{"items":[{"idMovimiento":1,"idIngrediente":1,"tipoMovimiento":"EntradaCompra","cantidadDelta":2,"stockAnterior":0,"stockPosterior":2,"idUsuario":1,"motivo":null,"fechaUtc":"2026-01-01T00:00:00Z"}],"cursorSiguiente":null}""";
        var ledger = await reader.MovimientosAsync(1, null, default);
        Assert.Equal(2m, Assert.Single(ledger.Items).Posterior);
        session.Json = session.Json.Replace("\"stockPosterior\":2", "\"stockPosterior\":3", StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.MovimientosAsync(1, null, default));
    }

    [Fact]
    public async Task Product_search_encodes_filter_and_preserves_cursor()
    {
        var session = new FakeSession
        {
            Json = """{"items":[{"idProducto":3,"nombre":"Café","estadoReceta":"Activa"}],"cursorSiguiente":"abc_123"}"""
        };
        var reader = new InventarioLecturaService(session);
        var first = await reader.ProductosFiltradosAsync(null, "Café & pan", default);
        Assert.Equal("abc_123", first.Siguiente);
        Assert.Contains("busqueda=Caf%C3%A9%20%26%20pan", session.Path, StringComparison.OrdinalIgnoreCase);
        await reader.ProductosFiltradosAsync(first.Siguiente, "Café & pan", default);
        Assert.Contains("cursor=abc_123", session.Path, StringComparison.Ordinal);
        session.Json = """{"items":[{"idProducto":3,"nombre":"Café","estadoReceta":"Desconocido"}],"cursorSiguiente":null}""";
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ProductosFiltradosAsync(null, "Café", default));
    }

    [Fact]
    public async Task Product_search_discards_a_response_after_filter_changes_and_reverts()
    {
        var session = new FakeSession();
        var reader = new SearchReader();
        using var vm = new InventarioLecturaViewModel(reader, session, true, () => { });
        vm.BusquedaProductos = "arroz";
        var pending = vm.ConsultarProductosCommand.ExecuteAsync(null);
        vm.BusquedaProductos = "pollo";
        vm.BusquedaProductos = "arroz";
        reader.Pending.SetResult(new([new ProductoRecetaLectura(1, "Anterior", "Activa")], "cursor"));
        await pending;
        Assert.Empty(vm.Productos);
        Assert.False(vm.HayMasProductos);
    }

    [Fact]
    public async Task Product_search_uses_next_cursor_and_invalidates_it_when_filter_changes()
    {
        var session = new FakeSession();
        var reader = new SearchReader { Immediate = true };
        using var vm = new InventarioLecturaViewModel(reader, session, true, () => { });
        vm.BusquedaProductos = "arroz";
        await vm.ConsultarProductosCommand.ExecuteAsync(null);
        Assert.True(vm.HayMasProductos);
        await vm.SiguientesProductosCommand.ExecuteAsync(null);
        Assert.Equal("next_1", reader.LastCursor);
        Assert.Equal("arroz", reader.LastSearch);
        vm.BusquedaProductos = "pollo";
        Assert.False(vm.HayMasProductos);
        await vm.SiguientesProductosCommand.ExecuteAsync(null);
        Assert.Equal(2, reader.Calls);
    }

    [Fact]
    public async Task Late_read_is_not_applied_after_session_is_paused()
    {
        var session = new FakeSession();
        var reader = new DelayedReader();
        using var vm = new InventarioLecturaViewModel(reader, session, false, () => { });
        var pending = vm.ConsultarIngredientesCommand.ExecuteAsync(null);
        session.ChangeState(SessionLifecycleState.Paused);
        session.ChangeState(SessionLifecycleState.Active);
        reader.Pending.SetResult(new([new IngredienteLectura(1, "Carne", "lb", 1, 0, true, "Normal")], null));
        await pending;
        Assert.Empty(vm.Ingredientes);
        Assert.False(vm.PuedeVerRecetas);
    }

    [Fact]
    public async Task Ingredient_read_is_discarded_if_filter_changes_and_returns_to_original()
    {
        var session = new FakeSession();
        var reader = new DelayedReader();
        using var vm = new InventarioLecturaViewModel(reader, session, false, () => { });
        var pending = vm.ConsultarIngredientesCommand.ExecuteAsync(null);
        vm.BusquedaIngredientes = "pollo";
        vm.BusquedaIngredientes = "";
        reader.Pending.SetResult(new([new IngredienteLectura(1, "Carne", "lb", 1, 0, true, "Normal", "1")], null));
        await pending;
        Assert.Empty(vm.Ingredientes);
    }

    private sealed class DelayedReader : IInventarioLecturaService
    {
        public TaskCompletionSource<InventarioPaginaLectura<IngredienteLectura>> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<InventarioPaginaLectura<IngredienteLectura>> IngredientesAsync(string? cursor, CancellationToken ct) => Pending.Task;
        public Task<InventarioPaginaLectura<ProductoRecetaLectura>> ProductosAsync(string? cursor, CancellationToken ct) => throw new NotSupportedException();
        public Task<InventarioPaginaLectura<ProductoRecetaLectura>> ProductosFiltradosAsync(string? cursor, string? busqueda, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> RecetaAsync(int productoId, CancellationToken ct) => throw new NotSupportedException();
        public Task<ResumenAlertasLectura> AlertasAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<InventarioPaginaLectura<IngredienteLectura>> IngredientesFiltradosAsync(string? cursor, string? estado, string? busqueda, CancellationToken ct) => throw new NotSupportedException();
        public Task<InventarioPaginaLectura<MovimientoLectura>> MovimientosAsync(int ingredientId, string? cursor, CancellationToken ct) => throw new NotSupportedException();
        public Task<RecetaLectura> DetalleRecetaAsync(int productoId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class SearchReader : IInventarioLecturaService
    {
        public TaskCompletionSource<InventarioPaginaLectura<ProductoRecetaLectura>> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Immediate { get; init; }
        public string? LastCursor { get; private set; }
        public string? LastSearch { get; private set; }
        public int Calls { get; private set; }
        public Task<InventarioPaginaLectura<ProductoRecetaLectura>> ProductosFiltradosAsync(string? cursor, string? busqueda, CancellationToken ct)
        {
            Calls++;
            LastCursor = cursor;
            LastSearch = busqueda;
            return Immediate ? Task.FromResult(new InventarioPaginaLectura<ProductoRecetaLectura>(
                [new(1, "Arroz", "Activa")], cursor is null ? "next_1" : null)) : Pending.Task;
        }
        public Task<InventarioPaginaLectura<ProductoRecetaLectura>> ProductosAsync(string? cursor, CancellationToken ct) => throw new NotSupportedException();
        public Task<InventarioPaginaLectura<IngredienteLectura>> IngredientesAsync(string? cursor, CancellationToken ct) => throw new NotSupportedException();
        public Task<InventarioPaginaLectura<IngredienteLectura>> IngredientesFiltradosAsync(string? cursor, string? estado, string? busqueda, CancellationToken ct) => throw new NotSupportedException();
        public Task<InventarioPaginaLectura<MovimientoLectura>> MovimientosAsync(int ingredientId, string? cursor, CancellationToken ct) => throw new NotSupportedException();
        public Task<RecetaLectura> DetalleRecetaAsync(int productoId, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> RecetaAsync(int productoId, CancellationToken ct) => throw new NotSupportedException();
        public Task<ResumenAlertasLectura> AlertasAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeSession : ISessionManager
    {
        public string Json { get; set; } = "{}";
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public string? Path { get; private set; }
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged;
        public bool HasSession => true;
        public SessionLifecycleState State { get; set; } = SessionLifecycleState.Active;
        public SessionSnapshot? CurrentSession => null;
        public void ChangeState(SessionLifecycleState state)
        {
            var previous = State;
            State = state;
            SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs(previous, state));
        }
        public Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default)
        {
            using var request = requestFactory();
            Assert.Equal(HttpMethod.Get, request.Method);
            Path = request.RequestUri?.OriginalString;
            Reads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json, Encoding.UTF8, "application/json") });
        }
        public Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default)
        {
            Writes++;
            throw new NotSupportedException();
        }
        public Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EvaluateState() { }
    }
}
