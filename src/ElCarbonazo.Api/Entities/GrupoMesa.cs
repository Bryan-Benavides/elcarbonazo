namespace ElCarbonazo.Api.Entities;

public sealed class GrupoMesa
{
    public int IdGrupo { get; set; }
    public int IdUsuarioCreacion { get; set; }
    public int? IdUsuarioCierre { get; set; }
    public DateTimeOffset FechaCreacion { get; set; }
    public DateTimeOffset? FechaCierre { get; set; }
    public bool Activo { get; set; } = true;
    public bool PermitePedidoTrasAnulacion { get; set; }

    public Usuario UsuarioCreacion { get; set; } = null!;
    public Usuario? UsuarioCierre { get; set; }
    public ICollection<Mesa> MesasActivas { get; set; } = [];
    public ICollection<GrupoMesaMiembro> Miembros { get; set; } = [];
    public ICollection<Pedido> Pedidos { get; set; } = [];
}
