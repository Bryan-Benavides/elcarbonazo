namespace ElCarbonazo.Api.Configuration;

public sealed record JwtSettings(
    string Key,
    string Issuer,
    string Audience,
    int ExpirationMinutes);
