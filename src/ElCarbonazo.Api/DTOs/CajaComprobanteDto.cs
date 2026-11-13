namespace ElCarbonazo.Api.DTOs;

public sealed record CajaComprobanteItemDto(long IdCobro, long NumeroComprobante, int IdPedido, int IdTurnoCaja,
    int IdUsuario, string MetodoPago, decimal Total, DateTimeOffset FechaHora);
public sealed record CajaComprobantePaginaDto(IReadOnlyList<CajaComprobanteItemDto> Items, string? CursorSiguiente);
public sealed record CajaComprobanteLineaDto(int Orden, int IdDetalleOrigen, string NombreProducto, short Cantidad,
    decimal PrecioUnitario, decimal Subtotal);
public sealed record CajaComprobanteDto(string Leyenda, long IdCobro, long NumeroComprobante, Guid IdOperacion,
    int IdPedido, int IdTurnoCaja, int IdUsuario, int? NumeroMesa, int? IdGrupo, IReadOnlyList<int> NumerosMesasGrupo,
    DateTimeOffset FechaHora, string MetodoPago, decimal Subtotal, decimal Impuestos, decimal Total,
    decimal? EfectivoRecibido, decimal? Vuelto, string? ReferenciaExterna, IReadOnlyList<CajaComprobanteLineaDto> Lineas);
