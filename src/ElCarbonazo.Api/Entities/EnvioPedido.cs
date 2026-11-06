namespace ElCarbonazo.Api.Entities;

public sealed class EnvioPedido
{
    public Guid EnvioId { get; set; }
    public int IdUsuario { get; set; }
    public string HashSolicitud { get; set; } = null!;
    public int IdComanda { get; set; }
    public short StatusCode { get; set; }
    public string Location { get; set; } = null!;
    public string ResponseJson { get; set; } = null!;
    public DateTimeOffset FechaHora { get; set; }

    public Usuario Usuario { get; set; } = null!;
    public Comanda Comanda { get; set; } = null!;
}
