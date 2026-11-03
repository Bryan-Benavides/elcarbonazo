namespace ElCarbonazo.Api.Configuration;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "ElCarbonazo.Api";
    public string Audience { get; set; } = "ElCarbonazo.Desktop";
    public int ExpirationMinutes { get; set; } = 15;
}
