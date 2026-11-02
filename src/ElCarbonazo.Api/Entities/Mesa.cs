namespace ElCarbonazo.Api.Entities;

public class Mesa
{
    public int IdMesa { get; set; }
    public int? IdGrupo { get; set; }
    public int Numero { get; set; }
    public int Capacidad { get; set; }
    public string Estado { get; set; } = null!;
}