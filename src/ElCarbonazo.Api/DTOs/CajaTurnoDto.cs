namespace ElCarbonazo.Api.DTOs;

public sealed record CajaTurnoResumenDto(int IdTurnoCaja, string Estado, DateTimeOffset FechaApertura, int IdUsuarioApertura,
    decimal FondoInicial, decimal TotalEfectivo, decimal TotalTarjeta, decimal TotalTransferencia, decimal EfectivoEsperado,
    decimal? EfectivoContado, decimal? Diferencia, int CantidadCuentasPendientes, DateTimeOffset? FechaCierre,
    int? IdUsuarioCierre, string? MotivoDiferencia);

public sealed record CajaTurnoActualDto(CajaTurnoResumenDto? Turno);
