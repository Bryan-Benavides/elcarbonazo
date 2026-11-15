using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Services;

public enum InventarioEscrituraEstado { Completada, Rechazada, Incierta, Bloqueada }
public sealed record InventarioEscrituraResultado(InventarioEscrituraEstado Estado, string? Codigo = null, int? Id = null);

// One instance must outlive the inventory view; it intentionally has no GET-based unlock.
public sealed class InventarioEscrituraCoordinator : IDisposable
{
    private readonly ISessionManager _session;
    private readonly IInventarioSessionSender _sender;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _incierta;
    private long _stateGeneration;
    private bool _disposed;
    public bool ResultadoIncierto => Volatile.Read(ref _incierta) != 0;

    public InventarioEscrituraCoordinator(ISessionManager session, IInventarioSessionSender sender)
    {
        _session = session;
        _sender = sender;
        _session.SessionStateChanged += OnSessionStateChanged;
    }

    public async Task<InventarioEscrituraResultado> EnviarAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) return new(InventarioEscrituraEstado.Bloqueada);
        try
        {
            if (_disposed || ResultadoIncierto || _session.CurrentSession is not { Rol: "Jefa", IdUsuario: > 0 } actor ||
                _session.State is not (SessionLifecycleState.Active or SessionLifecycleState.Expiring))
                return new(InventarioEscrituraEstado.Bloqueada);
            var generation = _sender.InventarioSessionGeneration;
            var stateGeneration = Interlocked.Read(ref _stateGeneration);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await _sender.SendInventarioOnceAsync(actor.IdUsuario, generation, requestFactory, timeout.Token);
                if (!SessionMatches(actor.IdUsuario, generation, stateGeneration))
                    return Uncertain();
                if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
                {
                    var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: timeout.Token);
                    if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("id", out var id) ||
                        id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var number) || number <= 0 ||
                        !SessionMatches(actor.IdUsuario, generation, stateGeneration))
                        return Uncertain();
                    return new(InventarioEscrituraEstado.Completada, Id: number);
                }
                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or
                    HttpStatusCode.Conflict or HttpStatusCode.RequestEntityTooLarge)
                {
                    var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(cancellationToken: timeout.Token);
                    if (string.IsNullOrWhiteSpace(problem?.Code) || problem.Status != (int)response.StatusCode ||
                        !SessionMatches(actor.IdUsuario, generation, stateGeneration))
                        return Uncertain();
                    return new(InventarioEscrituraEstado.Rechazada, problem.Code);
                }
                return Uncertain();
            }
            catch (Exception)
            {
                return Uncertain();
            }
        }
        finally { _gate.Release(); }
    }

    private bool SessionMatches(int actor, long generation, long stateGeneration) =>
        !_disposed && _sender.InventarioSessionGeneration == generation &&
        Interlocked.Read(ref _stateGeneration) == stateGeneration && _session.CurrentSession is { Rol: "Jefa" } current &&
        current.IdUsuario == actor && _session.State is SessionLifecycleState.Active or SessionLifecycleState.Expiring;

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs args) => Interlocked.Increment(ref _stateGeneration);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.SessionStateChanged -= OnSessionStateChanged;
    }

    private InventarioEscrituraResultado Uncertain()
    {
        Interlocked.Exchange(ref _incierta, 1);
        return new(InventarioEscrituraEstado.Incierta);
    }
}
