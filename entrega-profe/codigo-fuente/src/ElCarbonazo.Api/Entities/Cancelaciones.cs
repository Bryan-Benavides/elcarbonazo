namespace ElCarbonazo.Api.Entities;

public sealed class ConsumoDetalle
{
    public int IdDetalle { get; set; }
    public int IdIngrediente { get; set; }
    public decimal Cantidad { get; set; }
}

public sealed class AnulacionPedido
{
    public long IdAnulacion { get; set; }
    public int IdPedido { get; set; }
    public int IdComanda { get; set; }
    public int? IdDetalle { get; set; }
    public int CantidadAnulada { get; set; }
    public decimal MontoRevertido { get; set; }
    public string EstadoComandaPrevio { get; set; } = string.Empty;
    public int IdUsuarioSolicita { get; set; }
    public int IdUsuarioAutoriza { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public bool InventarioRevertido { get; set; }
    public DateTimeOffset FechaUtc { get; set; }
}

public sealed class OperacionAnulacion
{
    public Guid IdOperacion { get; set; }
    public int IdUsuarioPropietario { get; set; }
    public string? HashIntencion { get; set; }
    public string Estado { get; set; } = string.Empty;
    public DateTimeOffset FechaCreacionUtc { get; set; }
    public DateTimeOffset? FechaTerminalUtc { get; set; }
    public short? StatusResultado { get; set; }
    public string? CodigoResultado { get; set; }
    public string? ResultadoJson { get; set; }
}

public sealed class AutorizacionAnulacion
{
    public long IdAutorizacion { get; set; }
    public Guid IdOperacion { get; set; }
    public Guid IdSesion { get; set; }
    public int IdUsuarioAutoriza { get; set; }
    public string HashToken { get; set; } = string.Empty;
    public DateTimeOffset FechaEmisionUtc { get; set; }
    public DateTimeOffset FechaExpiracionUtc { get; set; }
    public DateTimeOffset? FechaConsumoUtc { get; set; }
}

public sealed class AlertaAnulacion
{
    public long IdAnulacion { get; set; }
    public DateTimeOffset? FechaAcuseUtc { get; set; }
    public int? IdUsuarioAcuse { get; set; }
}

public sealed class LimiteAutorizacion
{
    public int IdUsuarioSolicita { get; set; }
    public string HashNombre { get; set; } = string.Empty;
    public int IntentosFallidos { get; set; }
    public DateTimeOffset? BloqueoHastaUtc { get; set; }
    public DateTimeOffset UltimaActividadUtc { get; set; }
}
