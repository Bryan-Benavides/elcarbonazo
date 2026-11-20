namespace ElCarbonazo.Desktop.Models;

public sealed record AnulacionKdsDto(
    long IdAnulacion,
    int IdPedido,
    int IdComanda,
    int? IdMesa,
    int? IdGrupo,
    string Texto,
    string Motivo,
    DateTimeOffset FechaUtc,
    DateTimeOffset? FechaAcuseUtc);

public sealed record AnulacionKdsPage(IReadOnlyList<AnulacionKdsDto> Items, string? CursorSiguiente);
