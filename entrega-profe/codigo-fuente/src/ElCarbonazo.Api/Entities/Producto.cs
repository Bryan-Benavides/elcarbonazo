namespace ElCarbonazo.Api.Entities;

public sealed class Producto
{
    public int IdProducto { get; set; }
    public string Nombre { get; set; } = null!;
    public string Categoria { get; set; } = null!;
    public decimal Precio { get; set; }
    public bool Disponible { get; set; }
    public int Version { get; set; } = 1;

    public ICollection<DetallePedido> DetallesPedido { get; set; } = [];
}
