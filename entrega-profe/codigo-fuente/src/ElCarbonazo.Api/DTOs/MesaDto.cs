namespace ElCarbonazo.Api.DTOs;

public sealed record MesaDto(
    int IdMesa,
    int? IdGrupo,
    int Numero,
    int Capacidad,
    string Estado,
    uint Version,
    ProximaReservacionDto? ProximaReservacion = null);
