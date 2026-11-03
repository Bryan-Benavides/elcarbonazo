using System.Security.Claims;
using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Entities;

namespace ElCarbonazo.Api.Security;

public sealed class AuditRecorder(
    AppDbContext context,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider) : IAuditRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(
        string eventType,
        string result,
        int? userId = null,
        Guid? sessionId = null,
        string? entityType = null,
        string? entityId = null,
        IReadOnlyDictionary<string, object?>? details = null)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var claims = httpContext?.User;
        userId ??= ParseIntClaim(claims?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? claims?.FindFirst("sub")?.Value);
        sessionId ??= ParseGuidClaim(claims?.FindFirst("sid")?.Value);

        var installationId = httpContext?.Request.Headers["X-Installation-Id"].FirstOrDefault();
        context.EventosAuditoria.Add(new EventoAuditoria
        {
            FechaHora = timeProvider.GetUtcNow(),
            TipoEvento = eventType,
            Resultado = result,
            IdUsuario = userId,
            IdSesion = sessionId,
            DireccionIp = httpContext?.Connection.RemoteIpAddress,
            IdInstalacion = ParseGuidClaim(installationId),
            TipoEntidad = entityType,
            IdEntidad = entityId,
            TraceId = httpContext?.TraceIdentifier ?? $"bootstrap-{Guid.NewGuid():N}",
            Detalles = JsonSerializer.Serialize(details ?? EmptyDetails, JsonOptions)
        });
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyDetails =
        new Dictionary<string, object?>();

    private static int? ParseIntClaim(string? value) =>
        int.TryParse(value, out var parsed) ? parsed : null;

    private static Guid? ParseGuidClaim(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;
}
