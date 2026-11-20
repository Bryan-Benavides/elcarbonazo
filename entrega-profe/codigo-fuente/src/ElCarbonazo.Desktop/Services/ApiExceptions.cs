using System.Net;
using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Services;

public sealed class ApiException(HttpStatusCode statusCode, ApiProblem? problem)
    : Exception(problem?.Detail ?? problem?.Title ?? $"La API respondió {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public ApiProblem? Problem { get; } = problem;
}

public sealed class SessionExpiredException : Exception;
public sealed class SessionPausedException : Exception;
public sealed class SecurityConnectionException(Exception innerException)
    : Exception("La conexión TLS no pudo validarse.", innerException);

public sealed class SecurityFailureEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}
