namespace ElCarbonazo.Api.Entities;

public sealed class OperacionCaja
{
    public Guid IdOperacion { get; set; }
    public int IdUsuarioPropietario { get; set; }
    public string? Tipo { get; set; }
    public short? VersionContrato { get; set; }
    public string? Argumentos { get; set; }
    public string? HashArgumentos { get; set; }
    public string Estado { get; set; } = null!;
    public DateTimeOffset FechaCreacion { get; set; }
    public DateTimeOffset? FechaTerminal { get; set; }
    public short? StatusResultado { get; set; }
    public string? CodigoResultado { get; set; }
    public string? ResultadoJson { get; set; }
    public int? IdUsuarioCancelacion { get; set; }
    public DateTimeOffset? FechaCancelacion { get; set; }

    public Usuario UsuarioPropietario { get; set; } = null!;
    public Usuario? UsuarioCancelacion { get; set; }
    public ICollection<TurnoCaja> TurnosApertura { get; set; } = [];
    public ICollection<TurnoCaja> TurnosCierre { get; set; } = [];
    public Cobro? Cobro { get; set; }
}
