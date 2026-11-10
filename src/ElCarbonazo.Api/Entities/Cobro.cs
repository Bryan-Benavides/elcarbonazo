namespace ElCarbonazo.Api.Entities;

public sealed class Cobro
{
    public long IdCobro { get; set; }
    public long NumeroComprobante { get; set; }
    public int IdPedido { get; set; }
    public int IdTurnoCaja { get; set; }
    public int IdUsuario { get; set; }
    public Guid IdOperacion { get; set; }
    public string MetodoPago { get; set; } = null!;
    public decimal Subtotal { get; set; }
    public decimal Impuestos { get; set; }
    public decimal Total { get; set; }
    public decimal? EfectivoRecibido { get; set; }
    public decimal? Vuelto { get; set; }
    public string? ReferenciaExterna { get; set; }
    public bool VerificacionExternaConfirmada { get; set; }
    public bool ReferenciaRepetidaConfirmada { get; set; }
    public DateTimeOffset FechaHora { get; set; }

    public Pedido Pedido { get; set; } = null!;
    public TurnoCaja TurnoCaja { get; set; } = null!;
    public Usuario Usuario { get; set; } = null!;
    public OperacionCaja Operacion { get; set; } = null!;
    public ICollection<ComprobanteCobroLinea> Lineas { get; set; } = [];
}
