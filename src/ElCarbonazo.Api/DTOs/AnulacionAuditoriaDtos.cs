namespace ElCarbonazo.Api.DTOs;

public sealed record AnulacionAuditoriaDto(long IdAnulacion, int IdPedido, int IdComanda, int? IdDetalle,
    int? IdMesa, int? IdGrupo, int CantidadAnulada, decimal MontoRevertido, int IdUsuarioSolicita,
    int IdUsuarioAutoriza, string Motivo, bool InventarioRevertido, DateTimeOffset FechaUtc);

public sealed record EventoAuditoriaDto(long IdEvento, DateTimeOffset FechaHora, string TipoEvento,
    string Resultado, int? IdUsuario, string? TipoEntidad, string? IdEntidad);

public sealed record AuditoriaPagina<T>(IReadOnlyList<T> Items, string? CursorSiguiente);
