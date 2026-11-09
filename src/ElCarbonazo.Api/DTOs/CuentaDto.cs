namespace ElCarbonazo.Api.DTOs;

public sealed record CuentaActivaDto(CuentaDto? Pedido);

public sealed record CuentaDto(
    int IdPedido, int? IdMesa, int? IdGrupo, int IdUsuario, DateTimeOffset FechaHora,
    decimal Subtotal, decimal Impuestos, decimal Total, string Estado,
    bool SoloLectura, string? Motivo, IReadOnlyList<CuentaComandaDto> Comandas);

public sealed record CuentaComandaDto(
    int IdComanda, int IdUsuario, DateTimeOffset FechaHora, string Estado, uint Version,
    IReadOnlyList<CuentaDetalleDto> Lineas);

public sealed record CuentaDetalleDto(
    int IdDetalle, int IdProducto, string NombreProducto, short Cantidad,
    decimal PrecioUnitario, decimal Subtotal, string Notas);
