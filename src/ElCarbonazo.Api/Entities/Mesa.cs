namespace ElCarbonazo.Api.Entities;

public class Mesa
{
    public int IdMesa { get; set; }
    public int? IdGrupo { get; set; }
    public int Numero { get; set; }
    public int Capacidad { get; set; }
    public string Estado { get; set; } = null!;
    public bool PermitePedidoTrasAnulacion { get; set; }
    public uint Version { get; set; }

    public GrupoMesa? GrupoActivo { get; set; }
    public ICollection<GrupoMesaMiembro> HistorialGrupos { get; set; } = [];
    public ICollection<Pedido> Pedidos { get; set; } = [];
    public ICollection<Reservacion> Reservaciones { get; set; } = [];
}
