namespace ElCarbonazo.Api.Domain;

public static class SessionSecurityPolicy
{
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan InactivityLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(8);
    public static readonly TimeSpan ActivityWriteInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan RefreshHistoryRetention = TimeSpan.FromDays(30);
}
