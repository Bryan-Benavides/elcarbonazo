using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Services;

public interface ISessionManager
{
    event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged;

    bool HasSession { get; }
    SessionLifecycleState State { get; }
    SessionSnapshot? CurrentSession { get; }

    Task<AuthSessionResponse> LoginAsync(string nombre, string contrasena, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> SendHumanAsync(Func<HttpRequestMessage> requestFactory, bool isRead, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> SendPollingAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default);
    Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default);
    Task ContinueSessionAsync(CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    Task LogoutForShutdownAsync(CancellationToken cancellationToken = default);
    void EvaluateState();
}

public interface ICajaSessionSender
{
    Task<HttpResponseMessage> SendCajaOnceAsync(int expectedActor, Func<HttpRequestMessage> requestFactory,
        bool isRead, CancellationToken cancellationToken = default);
}

public sealed class SessionManager : ISessionManager, ICajaSessionSender, IDisposable
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ActivityInterval = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IApiClientService _apiClient;
    private readonly IServerClock _serverClock;
    private readonly TimeProvider _timeProvider;
    private readonly DesktopJsonLogger _logger;
    private readonly object _sessionGate = new();
    private readonly object _refreshGate = new();
    private readonly object _activityGate = new();
    private readonly SemaphoreSlim _activitySendGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _deferredActivity;
    private Task<bool>? _refreshTask;
    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset _accessTokenExpiresAt;
    private DateTimeOffset _inactivityExpiresAt;
    private DateTimeOffset _absoluteSessionExpiresAt;
    private DateTimeOffset? _lastActivitySentAt;
    private int _idUsuario;
    private string? _nombre;
    private string? _rol;
    private SessionLifecycleState _state = SessionLifecycleState.SignedOut;
    private bool _disposed;

    public SessionManager(
        IApiClientService apiClient,
        IServerClock serverClock,
        DesktopJsonLogger logger,
        TimeProvider? timeProvider = null)
    {
        _apiClient = apiClient;
        _serverClock = serverClock;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _apiClient.SecurityFailure += OnTransportSecurityFailure;
    }

    public event EventHandler<SessionStateChangedEventArgs>? SessionStateChanged;

    public bool HasSession
    {
        get
        {
            lock (_sessionGate)
            {
                return StateKeepsSecrets(_state) &&
                    _accessToken is not null &&
                    _refreshToken is not null;
            }
        }
    }

    public SessionLifecycleState State
    {
        get { lock (_sessionGate) return _state; }
    }

    public SessionSnapshot? CurrentSession
    {
        get
        {
            lock (_sessionGate)
            {
                return !StateKeepsSecrets(_state) ||
                    _accessToken is null ||
                    _nombre is null ||
                    _rol is null
                    ? null
                    : new SessionSnapshot(
                        _idUsuario,
                        _nombre,
                        _rol,
                        _accessTokenExpiresAt,
                        _inactivityExpiresAt,
                        _absoluteSessionExpiresAt);
            }
        }
    }

    public async Task<AuthSessionResponse> LoginAsync(
        string nombre,
        string contrasena,
        CancellationToken cancellationToken = default)
    {
        if (State is SessionLifecycleState.SecurityBlocked or SessionLifecycleState.ShuttingDown)
        {
            throw new InvalidOperationException("El estado actual no permite iniciar una sesión.");
        }

        var sentAt = _timeProvider.GetUtcNow();
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(nombre, contrasena), options: JsonOptions)
        };
        using var response = await _apiClient.SendAsync(
            request,
            ApiRequestKind.Write,
            cancellationToken);
        var receivedAt = _timeProvider.GetUtcNow();
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateApiExceptionAsync(response, cancellationToken);
        }

        var session = await ReadRequiredAsync<AuthSessionResponse>(response, cancellationToken);
        if (!ApplySession(session, sentAt, receivedAt))
        {
            throw new InvalidOperationException("La sesión cambió de estado durante el inicio de sesión.");
        }
        _logger.Information("LoginSucceeded", "La sesión local fue iniciada.");
        return session;
    }

    public async Task<HttpResponseMessage> SendHumanAsync(
        Func<HttpRequestMessage> requestFactory,
        bool isRead,
        CancellationToken cancellationToken = default)
    {
        await NotifyHumanInteractionAsync(cancellationToken);
        var response = await SendAuthenticatedAttemptAsync(
            requestFactory,
            isRead ? ApiRequestKind.Read : ApiRequestKind.Write,
            polling: false,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized &&
            await HasProblemCodeAsync(response, "AUTH_ACCESS_TOKEN_EXPIRADO", cancellationToken))
        {
            response.Dispose();
            if (await RefreshSingleFlightAsync(cancellationToken))
            {
                return await SendAuthenticatedAttemptAsync(
                    requestFactory,
                    isRead ? ApiRequestKind.Read : ApiRequestKind.Write,
                    polling: false,
                    cancellationToken);
            }
            throw new SessionExpiredException();
        }

        return response;
    }

    public async Task<HttpResponseMessage> SendCajaOnceAsync(int expectedActor, Func<HttpRequestMessage> requestFactory,
        bool isRead, CancellationToken cancellationToken = default)
    {
        EvaluateState();
        using var request = requestFactory();
        lock (_sessionGate)
        {
            if (expectedActor <= 0 || _idUsuario != expectedActor || _accessToken is null ||
                _state is not (SessionLifecycleState.Active or SessionLifecycleState.Expiring) ||
                _serverClock.UtcNow >= _accessTokenExpiresAt ||
                _rol is not ("Jefa" or "Encargado de Caja"))
                throw new SessionExpiredException();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }
        // No refresh and no retry: a failed write may already have committed.
        return await _apiClient.SendAsync(request, isRead ? ApiRequestKind.Read : ApiRequestKind.Write, cancellationToken);
    }

    public async Task<HttpResponseMessage> SendPollingAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken = default)
    {
        EvaluateState();
        if (!HasSession)
        {
            throw new SessionExpiredException();
        }
        if (State == SessionLifecycleState.Paused)
        {
            throw new SessionPausedException();
        }

        if (_serverClock.UtcNow >= _accessTokenExpiresAt)
        {
            PauseSession();
            throw new SessionPausedException();
        }

        var response = await SendAuthenticatedAttemptAsync(
            requestFactory,
            ApiRequestKind.Read,
            polling: true,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized &&
            await HasProblemCodeAsync(response, "AUTH_ACCESS_TOKEN_EXPIRADO", cancellationToken))
        {
            response.Dispose();
            PauseSession();
            throw new SessionPausedException();
        }

        return response;
    }

    public async Task NotifyHumanInteractionAsync(CancellationToken cancellationToken = default)
    {
        EvaluateState();
        if (!HasSession)
        {
            return;
        }

        if (_accessTokenExpiresAt - _serverClock.UtcNow <= RefreshMargin &&
            !await RefreshSingleFlightAsync(cancellationToken))
        {
            return;
        }

        ScheduleActivityReport();
    }

    public async Task ContinueSessionAsync(CancellationToken cancellationToken = default)
    {
        EvaluateState();
        if (!HasSession)
        {
            return;
        }

        if (_accessTokenExpiresAt - _serverClock.UtcNow <= RefreshMargin &&
            !await RefreshSingleFlightAsync(cancellationToken))
        {
            return;
        }

        await ReportActivityAsync(cancellationToken);
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default) =>
        LogoutCoreAsync(ApiRequestKind.Write, cancellationToken);

    public Task LogoutForShutdownAsync(CancellationToken cancellationToken = default) =>
        LogoutCoreAsync(ApiRequestKind.ShutdownLogout, cancellationToken);

    public void EvaluateState()
    {
        SessionLifecycleState previous;
        SessionLifecycleState target;
        lock (_sessionGate)
        {
            previous = _state;
            if (previous is not (
                SessionLifecycleState.Active or
                SessionLifecycleState.Expiring or
                SessionLifecycleState.Paused))
            {
                return;
            }

            var now = _serverClock.UtcNow;
            if (now >= _inactivityExpiresAt || now >= _absoluteSessionExpiresAt)
            {
                target = SessionLifecycleState.Expired;
            }
            else if (previous == SessionLifecycleState.Paused)
            {
                return;
            }
            else
            {
                var isExpiring = _inactivityExpiresAt - now <= TimeSpan.FromMinutes(1) ||
                    _absoluteSessionExpiresAt - now <= TimeSpan.FromMinutes(5);
                target = isExpiring ? SessionLifecycleState.Expiring : SessionLifecycleState.Active;
            }

            if (previous == target)
            {
                return;
            }
            _state = target;
            if (!StateKeepsSecrets(target))
            {
                ClearSecretsCore();
            }
        }

        SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs(previous, target));
    }

    private async Task LogoutCoreAsync(
        ApiRequestKind requestKind,
        CancellationToken cancellationToken)
    {
        if (HasSession)
        {
            TransitionTo(SessionLifecycleState.ShuttingDown);
        }

        try
        {
            if (HasSession)
            {
                using var request = CreateAuthenticatedRequest(HttpMethod.Post, "api/auth/logout");
                using var response = await _apiClient.SendAsync(request, requestKind, cancellationToken);
                _logger.Information(
                    "LogoutRemoteCompleted",
                    $"La API respondió al cierre de sesión con estado {(int)response.StatusCode}.");
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
                TaskCanceledException or
                OperationCanceledException or
                SecurityConnectionException)
        {
            _logger.Warning("LogoutBestEffortFailed", "No fue posible confirmar el cierre remoto de la sesión.");
        }
        finally
        {
            if (State != SessionLifecycleState.SecurityBlocked)
            {
                TransitionTo(SessionLifecycleState.SignedOut);
            }
        }
    }

    private async Task<bool> RefreshSingleFlightAsync(CancellationToken cancellationToken)
    {
        Task<bool> task;
        lock (_refreshGate)
        {
            if (_refreshTask is null)
            {
                task = RefreshCoreAsync(_lifetime.Token);
                _refreshTask = task;
                _ = ClearRefreshTaskWhenCompletedAsync(task);
            }
            else task = _refreshTask;
        }

        return await task.WaitAsync(cancellationToken);
    }

    private async Task ClearRefreshTaskWhenCompletedAsync(Task<bool> task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Los consumidores observan la excepción original; aquí solo se libera el slot single-flight.
        }
        finally
        {
            lock (_refreshGate)
            {
                if (ReferenceEquals(_refreshTask, task))
                {
                    _refreshTask = null;
                }
            }
        }
    }

    private async Task<bool> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        string? refreshToken;
        lock (_sessionGate) refreshToken = _refreshToken;
        if (refreshToken is null)
        {
            return false;
        }

        var sentAt = _timeProvider.GetUtcNow();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh")
            {
                Content = JsonContent.Create(new RefreshRequest(refreshToken), options: JsonOptions)
            };
            using var response = await _apiClient.SendAsync(
                request,
                ApiRequestKind.Write,
                cancellationToken);
            var receivedAt = _timeProvider.GetUtcNow();
            if (!response.IsSuccessStatusCode)
            {
                var problem = await ReadProblemAsync(response, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _logger.Warning("RefreshRejected", "La API rechazó la renovación.", problem?.TraceId);
                    TransitionTo(SessionLifecycleState.Expired);
                    return false;
                }

                throw new ApiException(response.StatusCode, problem);
            }

            var session = await ReadRequiredAsync<AuthSessionResponse>(response, cancellationToken);
            if (!ApplySession(session, sentAt, receivedAt, refreshToken))
            {
                return false;
            }
            _logger.Information("RefreshSucceeded", "La sesión local fue renovada.");
            return true;
        }
        catch (SecurityConnectionException)
        {
            throw;
        }
    }

    private void ScheduleActivityReport()
    {
        lock (_activityGate)
        {
            if (_deferredActivity is not null)
            {
                return;
            }

            var now = _serverClock.UtcNow;
            var delay = _lastActivitySentAt is null
                ? TimeSpan.Zero
                : ActivityInterval - (now - _lastActivitySentAt.Value);
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            _deferredActivity = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var token = _deferredActivity.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, _timeProvider, token);
                    }
                    await ReportActivityAsync(token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (HttpRequestException)
                {
                    _logger.Warning("ActivityReportConnectionFailed", "No fue posible reportar actividad por un fallo de red local.");
                }
                catch (ApiException exception)
                {
                    _logger.Warning("ActivityReportRejected", "La API rechazó el reporte de actividad.", exception.Problem?.TraceId);
                }
                catch (SecurityConnectionException)
                {
                    // El transporte ya emitió el bloqueo no recuperable.
                }
                finally
                {
                    lock (_activityGate)
                    {
                        _deferredActivity?.Dispose();
                        _deferredActivity = null;
                    }
                }
            }, token);
        }
    }

    private async Task ReportActivityAsync(CancellationToken cancellationToken)
    {
        await _activitySendGate.WaitAsync(cancellationToken);
        try
        {
            if (!HasSession ||
                (_lastActivitySentAt is not null &&
                 _serverClock.UtcNow - _lastActivitySentAt.Value < ActivityInterval))
            {
                return;
            }

            var sentAt = _timeProvider.GetUtcNow();
            using var request = CreateAuthenticatedRequest(HttpMethod.Post, "api/auth/activity");
            using var response = await _apiClient.SendAsync(
                request,
                ApiRequestKind.Write,
                cancellationToken);
            var receivedAt = _timeProvider.GetUtcNow();
            if (!response.IsSuccessStatusCode)
            {
                var problem = await ReadProblemAsync(response, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    TransitionTo(SessionLifecycleState.Expired);
                    return;
                }
                throw new ApiException(response.StatusCode, problem);
            }

            var activity = await ReadRequiredAsync<ActivityResponse>(response, cancellationToken);
            _serverClock.Synchronize(activity.ServerTime, sentAt, receivedAt);
            lock (_sessionGate)
            {
                _inactivityExpiresAt = activity.InactivityExpiresAt;
                _absoluteSessionExpiresAt = activity.AbsoluteSessionExpiresAt;
                _lastActivitySentAt = _serverClock.UtcNow;
            }
            EvaluateState();
        }
        finally
        {
            _activitySendGate.Release();
        }
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAttemptAsync(
        Func<HttpRequestMessage> requestFactory,
        ApiRequestKind requestKind,
        bool polling,
        CancellationToken cancellationToken)
    {
        EvaluateState();
        if (!HasSession)
        {
            throw new SessionExpiredException();
        }

        using var request = requestFactory();
        AddAuthentication(request);
        try
        {
            var response = await _apiClient.SendAsync(request, requestKind, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized &&
                await HasProblemCodeAsync(response, "AUTH_SESION_EXPIRADA", cancellationToken))
            {
                response.Dispose();
                TransitionTo(SessionLifecycleState.Expired);
                throw new SessionExpiredException();
            }
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!polling)
            {
                _logger.Warning("RequestTimeout", "Una solicitud iniciada por el usuario superó su tiempo límite.");
            }
            throw;
        }
    }

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        AddAuthentication(request);
        return request;
    }

    private void AddAuthentication(HttpRequestMessage request)
    {
        lock (_sessionGate)
        {
            if (_accessToken is null)
            {
                throw new SessionExpiredException();
            }
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }
    }

    private bool ApplySession(
        AuthSessionResponse session,
        DateTimeOffset sentAt,
        DateTimeOffset receivedAt,
        string? expectedRefreshToken = null)
    {
        SessionLifecycleState target;
        SessionLifecycleState previous;
        lock (_sessionGate)
        {
            previous = _state;
            if (expectedRefreshToken is not null &&
                (!string.Equals(_refreshToken, expectedRefreshToken, StringComparison.Ordinal) ||
                 previous is not (SessionLifecycleState.Active or SessionLifecycleState.Expiring or SessionLifecycleState.Paused)))
            {
                return false;
            }
            if (previous is SessionLifecycleState.SecurityBlocked or SessionLifecycleState.ShuttingDown)
            {
                return false;
            }
            _serverClock.Synchronize(session.ServerTime, sentAt, receivedAt);
            var now = _serverClock.UtcNow;
            target = session.InactivityExpiresAt - now <= TimeSpan.FromMinutes(1) ||
                session.AbsoluteSessionExpiresAt - now <= TimeSpan.FromMinutes(5)
                ? SessionLifecycleState.Expiring
                : SessionLifecycleState.Active;
            if (!IsTransitionAllowed(previous, target) && previous != target)
            {
                throw new InvalidOperationException($"Transición de sesión no válida: {previous} -> {target}.");
            }

            _accessToken = session.AccessToken;
            _refreshToken = session.RefreshToken;
            _accessTokenExpiresAt = session.AccessTokenExpiresAt;
            _inactivityExpiresAt = session.InactivityExpiresAt;
            _absoluteSessionExpiresAt = session.AbsoluteSessionExpiresAt;
            _lastActivitySentAt = _serverClock.UtcNow;
            _idUsuario = session.IdUsuario;
            _nombre = session.Nombre;
            _rol = session.Rol;
            _state = target;
        }
        if (previous != target)
        {
            SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs(previous, target));
        }
        return true;
    }

    private void PauseSession()
    {
        var state = State;
        if (state == SessionLifecycleState.Paused)
        {
            return;
        }
        if (state is not (SessionLifecycleState.Active or SessionLifecycleState.Expiring))
        {
            return;
        }
        TransitionTo(SessionLifecycleState.Paused);
        _logger.Information("SessionPaused", "El access token venció durante una tarea automática.");
    }

    private void OnTransportSecurityFailure(object? sender, SecurityFailureEventArgs args)
    {
        _lifetime.Cancel();
        TransitionTo(SessionLifecycleState.SecurityBlocked, args.Message);
    }

    private void TransitionTo(SessionLifecycleState target, string? message = null)
    {
        SessionLifecycleState previous;
        lock (_sessionGate)
        {
            previous = _state;
            if (previous == target)
            {
                return;
            }
            if (!IsTransitionAllowed(previous, target))
            {
                if (previous is
                    SessionLifecycleState.SecurityBlocked or
                    SessionLifecycleState.ShuttingDown or
                    SessionLifecycleState.Expired or
                    SessionLifecycleState.SignedOut)
                {
                    return;
                }
                throw new InvalidOperationException($"Transición de sesión no válida: {previous} -> {target}.");
            }

            _state = target;
            if (!StateKeepsSecrets(target))
            {
                ClearSecretsCore();
            }
        }

        SessionStateChanged?.Invoke(this, new SessionStateChangedEventArgs(previous, target, message));
    }

    private void ClearSecretsCore()
    {
        _accessToken = null;
        _refreshToken = null;
        _nombre = null;
        _rol = null;
        _idUsuario = 0;
        _accessTokenExpiresAt = default;
        _inactivityExpiresAt = default;
        _absoluteSessionExpiresAt = default;
        _lastActivitySentAt = null;
        _deferredActivity?.Cancel();
    }

    private static bool StateKeepsSecrets(SessionLifecycleState state) => state is
        SessionLifecycleState.Active or
        SessionLifecycleState.Expiring or
        SessionLifecycleState.Paused or
        SessionLifecycleState.ShuttingDown;

    private static bool IsTransitionAllowed(
        SessionLifecycleState previous,
        SessionLifecycleState target) => target switch
        {
            SessionLifecycleState.SecurityBlocked => true,
            SessionLifecycleState.SignedOut => previous != SessionLifecycleState.SecurityBlocked,
            SessionLifecycleState.ShuttingDown => StateKeepsSecrets(previous),
            SessionLifecycleState.Expired => previous is
                SessionLifecycleState.Active or
                SessionLifecycleState.Expiring or
                SessionLifecycleState.Paused,
            SessionLifecycleState.Paused => previous is
                SessionLifecycleState.Active or
                SessionLifecycleState.Expiring,
            SessionLifecycleState.Active or SessionLifecycleState.Expiring => previous is
                SessionLifecycleState.SignedOut or
                SessionLifecycleState.Active or
                SessionLifecycleState.Expiring or
                SessionLifecycleState.Paused or
                SessionLifecycleState.Expired,
            _ => false
        };

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
        ?? throw new InvalidDataException("La API devolvió una respuesta vacía o inválida.");

    private static async Task<bool> HasProblemCodeAsync(
        HttpResponseMessage response,
        string expectedCode,
        CancellationToken cancellationToken)
    {
        var problem = await ReadProblemAsync(response, cancellationToken);
        return string.Equals(problem?.Code, expectedCode, StringComparison.Ordinal);
    }

    private static async Task<ApiProblem?> ReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var contentHeaders = response.Content.Headers
                .Select(header => new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value))
                .ToArray();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var replacement = new ByteArrayContent(bytes);
            foreach (var header in contentHeaders)
            {
                replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            response.Content = replacement;
            return JsonSerializer.Deserialize<ApiProblem>(bytes, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<ApiException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        new(response.StatusCode, await ReadProblemAsync(response, cancellationToken));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _apiClient.SecurityFailure -= OnTransportSecurityFailure;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _deferredActivity?.Dispose();
    }
}
