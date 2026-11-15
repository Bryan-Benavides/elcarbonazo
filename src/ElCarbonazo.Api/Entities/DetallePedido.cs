namespace ElCarbonazo.Api.Entities;

public sealed class DetallePedido
{
    public int IdDetalle { get; set; }
    public int IdComanda { get; set; }
    public int IdProducto { get; set; }
    public string NombreProducto { get; set; } = null!;
    public short Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal { get; set; }
    public string Notas { get; set; } = string.Empty;
    public bool Cancelado { get; set; }

    public Comanda Comanda { get; set; } = null!;
    public Producto Producto { get; set; } = null!;
}
