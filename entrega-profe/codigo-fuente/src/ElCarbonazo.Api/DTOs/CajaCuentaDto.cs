namespace ElCarbonazo.Api.DTOs;

public sealed record CajaLineaDto(int IdDetalle, int IdProducto, string NombreProducto, short Cantidad, decimal PrecioUnitario, decimal Subtotal);
public sealed record CajaComandaDto(int IdComanda, string Estado, uint Version, IReadOnlyList<CajaLineaDto> Lineas);
public sealed record CajaCuentaDto(int IdPedido, int? IdMesa, int? NumeroMesa, int? IdGrupo, IReadOnlyList<int> NumerosMesasGrupo,
    string Estado, decimal Subtotal, decimal Impuestos, decimal Total, bool Cobrable, string? Motivo,
    string? HuellaLineas, IReadOnlyList<CajaComandaDto> Comandas);
public sealed record CajaCuentasPaginaDto(IReadOnlyList<CajaCuentaDto> Items, string? CursorSiguiente);
