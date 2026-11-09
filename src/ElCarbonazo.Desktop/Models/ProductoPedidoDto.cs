namespace ElCarbonazo.Desktop.Models;

public sealed record ProductoPedidoDto(
    int IdProducto, string Nombre, string Categoria, decimal Precio, bool Disponible, int Version);

public sealed record PaginaProductosDto(IReadOnlyList<ProductoPedidoDto> Items, string? CursorSiguiente);
