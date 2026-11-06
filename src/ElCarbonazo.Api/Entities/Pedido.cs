namespace ElCarbonazo.Api.Entities;

public sealed class Pedido
{
    public int IdPedido { get; set; }
    public int? IdMesa { get; set; }
    public int? IdGrupo { get; set; }
    public int IdUsuario { get; set; }
    public DateTimeOffset FechaHora { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Impuestos { get; set; }
    public decimal Total { get; set; }
    public string Estado { get; set; } = null!;

    public Mesa? Mesa { get; set; }
    public GrupoMesa? Grupo { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public ICollection<Comanda> Comandas { get; set; } = [];
}
