using System.Security.Authentication;
using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Services;

public interface IApiClientService
{
    event EventHandler<SecurityFailureEventArgs>? SecurityFailure;

    Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        ApiRequestKind requestKind,
        CancellationToken cancellationToken = default);
}

public enum ApiRequestKind
{
    Read,
    Write,
    ShutdownLogout
}

public sealed class ApiClientService : IApiClientService, IDisposable
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShutdownLogoutTimeout = TimeSpan.FromSeconds(2);

    private readonly HttpClient _httpClient;
    private readonly Guid _installationId;
    private readonly DesktopJsonLogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public ApiClientService(
        DesktopConfiguration configuration,
        DesktopJsonLogger logger,
        HttpMessageHandler? handler = null)
    {
        _installationId = configuration.IdInstalacion;
        _logger = logger;
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _httpClient.BaseAddress = configuration.ApiBaseUrl;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
    }

    public event EventHandler<SecurityFailureEventArgs>? SecurityFailure;

    public async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        ApiRequestKind requestKind,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        request.Headers.Remove("X-Installation-Id");
        request.Headers.Add("X-Installation-Id", _installationId.ToString());
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        timeoutSource.CancelAfter(GetTimeout(requestKind));
        try
        {
            return await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                timeoutSource.Token);
        }
        catch (HttpRequestException exception) when (IsTlsFailure(exception))
        {
            _logger.Error("TlsSecurityFailure", "La validación TLS impidió la conexión con la API.");
            SecurityFailure?.Invoke(this, new SecurityFailureEventArgs(
                "No se pudo validar la identidad segura del servidor. Contacte al administrador."));
            throw new SecurityConnectionException(exception);
        }
    }

    private static TimeSpan GetTimeout(ApiRequestKind requestKind) => requestKind switch
    {
        ApiRequestKind.Read => ReadTimeout,
        ApiRequestKind.Write => WriteTimeout,
        ApiRequestKind.ShutdownLogout => ShutdownLogoutTimeout,
        _ => throw new ArgumentOutOfRangeException(nameof(requestKind), requestKind, null)
    };

    private static bool IsTlsFailure(Exception exception)
    {
        if (exception is HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError })
        {
            return true;
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
            {
                return true;
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _httpClient.Dispose();
    }
}
