namespace ElCarbonazo.Api.Entities;

public sealed class GrupoMesaMiembro
{
    public int IdGrupo { get; set; }
    public int IdMesa { get; set; }
    public DateTimeOffset FechaAsignacion { get; set; }

    public GrupoMesa Grupo { get; set; } = null!;
    public Mesa Mesa { get; set; } = null!;
}
