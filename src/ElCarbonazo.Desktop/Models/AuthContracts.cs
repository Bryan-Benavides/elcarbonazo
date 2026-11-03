namespace ElCarbonazo.Desktop.Models;

public sealed record LoginRequest(string Nombre, string Contrasena);

public sealed record RefreshRequest(string RefreshToken);

public sealed record AuthSessionResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset InactivityExpiresAt,
    DateTimeOffset AbsoluteSessionExpiresAt,
    DateTimeOffset ServerTime,
    int IdUsuario,
    string Nombre,
    string Rol);

public sealed record ActivityResponse(
    DateTimeOffset InactivityExpiresAt,
    DateTimeOffset AbsoluteSessionExpiresAt,
    DateTimeOffset ServerTime);

public sealed record ApiProblem(int? Status, string? Code, string? Title, string? Detail, string? TraceId);
