namespace ElCarbonazo.Api.Entities;

public sealed class ComprobanteCobroLinea
{
    public long IdComprobanteCobroLinea { get; set; }
    public long IdCobro { get; set; }
    public int Orden { get; set; }
    public int IdDetalleOrigen { get; set; }
    public string NombreProducto { get; set; } = null!;
    public short Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal { get; set; }

    public Cobro Cobro { get; set; } = null!;
}
