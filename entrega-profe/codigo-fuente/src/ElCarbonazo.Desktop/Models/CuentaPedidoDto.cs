namespace ElCarbonazo.Desktop.Models;

public sealed record CuentaActivaPedidoDto(CuentaPedidoDto? Pedido);

public sealed record CuentaPedidoDto(
    int IdPedido, int? IdMesa, int? IdGrupo, int IdUsuario, DateTimeOffset FechaHora,
    decimal Subtotal, decimal Impuestos, decimal Total, string Estado,
    bool SoloLectura, string? Motivo, IReadOnlyList<ComandaPedidoDto> Comandas);

public sealed record ComandaPedidoDto(
    int IdComanda, int IdUsuario, DateTimeOffset FechaHora, string Estado, uint Version,
    IReadOnlyList<DetallePedidoDto> Lineas);

public sealed record DetallePedidoDto(
    int IdDetalle, int IdProducto, string NombreProducto, short Cantidad,
    decimal PrecioUnitario, decimal Subtotal, string Notas);
