namespace ElCarbonazo.Api.Security;

public interface IAuditRecorder
{
    void Record(
        string eventType,
        string result,
        int? userId = null,
        Guid? sessionId = null,
        string? entityType = null,
        string? entityId = null,
        IReadOnlyDictionary<string, object?>? details = null);
}
