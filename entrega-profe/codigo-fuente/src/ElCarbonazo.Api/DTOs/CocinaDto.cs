namespace ElCarbonazo.Api.DTOs;

public sealed record CocinaLineaDto(int IdDetalle, string NombreProducto, short Cantidad, string Notas);

public sealed record CocinaComandaDto(
    int IdComanda, int IdPedido, int? IdMesa, int? NumeroMesa, int? IdGrupo,
    int IdUsuario, DateTimeOffset FechaHora, string Estado, uint Version,
    IReadOnlyList<CocinaLineaDto> Lineas);

public sealed record CocinaPaginaDto(IReadOnlyList<CocinaComandaDto> Items, string? CursorSiguiente);

public sealed record CambiarComandaEstadoRequest(string? Estado, uint? VersionComanda);
