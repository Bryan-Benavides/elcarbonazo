namespace ElCarbonazo.Api.Entities;

public sealed class TurnoCaja
{
    public int IdTurnoCaja { get; set; }
    public string Estado { get; set; } = null!;
    public DateTimeOffset FechaApertura { get; set; }
    public int IdUsuarioApertura { get; set; }
    public decimal FondoInicial { get; set; }
    public Guid IdOperacionApertura { get; set; }
    public DateTimeOffset? FechaCierre { get; set; }
    public int? IdUsuarioCierre { get; set; }
    public Guid? IdOperacionCierre { get; set; }
    public decimal? EfectivoEsperado { get; set; }
    public decimal? EfectivoContado { get; set; }
    public decimal? Diferencia { get; set; }
    public decimal? TotalEfectivo { get; set; }
    public decimal? TotalTarjeta { get; set; }
    public decimal? TotalTransferencia { get; set; }
    public int? CantidadCuentasPendientes { get; set; }
    public string? MotivoDiferencia { get; set; }

    public Usuario UsuarioApertura { get; set; } = null!;
    public Usuario? UsuarioCierre { get; set; }
    public OperacionCaja OperacionApertura { get; set; } = null!;
    public OperacionCaja? OperacionCierre { get; set; }
    public ICollection<Cobro> Cobros { get; set; } = [];
}
