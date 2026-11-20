using System.Net.Http.Headers;
using System.Net.Http.Json;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;
using ElCarbonazo.Desktop.ViewModels;

internal static class InventarioSmoke
{
    public static async Task RunAsync(HttpClient client, CancellationToken token)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var login = await client.PostAsJsonAsync("api/auth/login", new { nombre = "jefa central", contrasena = "correct horse battery staple" }, token);
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthSessionResponse>(cancellationToken: token) ?? throw new InvalidDataException("Sesión inválida.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var session = new TestSession(client, auth);
        using var coordinator = new InventarioEscrituraCoordinator(session, session);
        using var view = new InventarioLecturaViewModel(new InventarioLecturaService(session), session, true, () => { }, coordinator);
        view.NombreNuevo = "Insumo temporal de prueba";
        view.UnidadNueva = "lb";
        view.MinimoNuevo = "2";
        view.ConfirmarIngrediente = true;
        await view.CrearIngredienteCommand.ExecuteAsync(null);
        Require(view.EstadoEscritura.Contains("confirmada", StringComparison.OrdinalIgnoreCase), "No se creó ingrediente.");
        await view.ConsultarIngredientesCommand.ExecuteAsync(null);
        view.IngredienteSeleccionado = view.Ingredientes.Single(x => x.Nombre == "Insumo temporal de prueba");
        view.CantidadMovimiento = "5";
        view.ConfirmarMovimiento = true;
        await view.RegistrarMovimientoCommand.ExecuteAsync(null);
        Require(view.EstadoEscritura.Contains("confirmada", StringComparison.OrdinalIgnoreCase), "No se registró entrada.");
        await view.ConsultarIngredientesCommand.ExecuteAsync(null);
        view.IngredienteSeleccionado = view.Ingredientes.Single(x => x.Nombre == "Insumo temporal de prueba");
        Require(view.IngredienteSeleccionado.Stock == 5, "Stock no coincide.");
        view.NombreEdicion = "Insumo temporal editado";
        view.ConfirmarEdicion = true;
        await view.ActualizarIngredienteCommand.ExecuteAsync(null);
        Require(view.EstadoEscritura.Contains("confirmada", StringComparison.OrdinalIgnoreCase), "No se actualizó ingrediente.");
        await view.ConsultarIngredientesCommand.ExecuteAsync(null);
        view.IngredienteSeleccionado = view.Ingredientes.Single(x => x.Nombre == "Insumo temporal editado");
        await view.ConsultarMovimientosCommand.ExecuteAsync(null);
        Require(view.Movimientos.Any(x => x.Tipo == "EntradaCompra"), "No se encontró movimiento.");
        await view.ConsultarProductosCommand.ExecuteAsync(null);
        view.ProductoSeleccionado = view.Productos.First();
        await view.ConsultarRecetaCommand.ExecuteAsync(null);
        view.CantidadReceta = "0.25";
        view.AgregarLineaRecetaCommand.Execute(null);
        view.ConfirmarReceta = true;
        await view.GuardarRecetaCommand.ExecuteAsync(null);
        Require(view.EstadoEscritura.Contains("confirmada", StringComparison.OrdinalIgnoreCase), "No se actualizó receta.");
        Console.WriteLine("INVENTARIO SMOKE: creación, entrada, edición, kardex y receta mediante ViewModel/REST y PostgreSQL temporal; sin ventana ni render, sesión adaptada.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private sealed class TestSession(HttpClient client, AuthSessionResponse auth) : ISessionManager, IInventarioSessionSender
    {
        public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged { add { } remove { } }
        public bool HasSession => true;
        public SessionLifecycleState State => SessionLifecycleState.Active;
        public SessionSnapshot? CurrentSession => new(auth.IdUsuario, auth.Nombre, auth.Rol, auth.AccessTokenExpiresAt, auth.InactivityExpiresAt, auth.AbsoluteSessionExpiresAt);
        public long InventarioSessionGeneration => 1;
        public async Task<HttpResponseMessage> SendInventarioOnceAsync(int expectedActor, long expectedGeneration,
            Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default)
        {
            if (expectedActor != auth.IdUsuario || expectedGeneration != 1) throw new InvalidOperationException();
            using var request = requestFactory();
            return await client.SendAsync(request, cancellationToken);
        }
        public async Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default)
        {
            using var request = requestFactory();
            return await client.SendAsync(request, cancellationToken);
        }
        public Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EvaluateState() { }
    }
}
