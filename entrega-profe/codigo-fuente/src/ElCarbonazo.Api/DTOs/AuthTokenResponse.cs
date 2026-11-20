namespace ElCarbonazo.Api.DTOs;

public sealed record AuthTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset InactivityExpiresAt,
    DateTimeOffset AbsoluteSessionExpiresAt,
    DateTimeOffset ServerTime,
    int IdUsuario,
    string Nombre,
    string Rol);
