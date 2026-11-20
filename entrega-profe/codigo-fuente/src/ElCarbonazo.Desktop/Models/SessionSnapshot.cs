namespace ElCarbonazo.Desktop.Models;

public sealed record SessionSnapshot(
    int IdUsuario,
    string Nombre,
    string Rol,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset InactivityExpiresAt,
    DateTimeOffset AbsoluteSessionExpiresAt);
