namespace ElCarbonazo.Api.DTOs;

public sealed record LoginResponse(
    string Token,
    int IdUsuario,
    string Nombre,
    string Rol);
