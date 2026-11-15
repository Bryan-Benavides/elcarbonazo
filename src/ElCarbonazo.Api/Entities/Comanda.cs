namespace ElCarbonazo.Api.Entities;

public sealed class Comanda
{
    public int IdComanda { get; set; }
    public int IdPedido { get; set; }
    public int IdUsuario { get; set; }
    public DateTimeOffset FechaHora { get; set; }
    public string Estado { get; set; } = null!;
    public uint Version { get; set; }
    public long RevisionDetalles { get; set; } = 1;

    public Pedido Pedido { get; set; } = null!;
    public Usuario Usuario { get; set; } = null!;
    public ICollection<DetallePedido> Detalles { get; set; } = [];
    public EnvioPedido? Envio { get; set; }
}
