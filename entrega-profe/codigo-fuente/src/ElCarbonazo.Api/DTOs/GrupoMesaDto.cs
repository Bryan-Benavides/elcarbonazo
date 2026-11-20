namespace ElCarbonazo.Api.DTOs;

public sealed record GrupoMesaDto(
    int IdGrupo,
    int IdUsuarioCreacion,
    DateTimeOffset FechaCreacion,
    bool Activo,
    IReadOnlyList<MesaDto> Mesas);
