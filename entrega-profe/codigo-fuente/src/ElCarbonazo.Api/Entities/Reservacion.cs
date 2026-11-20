namespace ElCarbonazo.Api.Entities;

public sealed class Reservacion
{
    public int IdReservacion { get; set; }
    public int IdMesa { get; set; }
    public string NombreReferencia { get; set; } = null!;
    public short CantidadPersonas { get; set; }
    public DateTimeOffset InicioUtc { get; set; }
    public DateTimeOffset FinUtc { get; set; }
    public string Estado { get; set; } = "Activa";
    public DateTimeOffset CreadaUtc { get; set; }
    public DateTimeOffset? CanceladaUtc { get; set; }

    public Mesa Mesa { get; set; } = null!;
}
