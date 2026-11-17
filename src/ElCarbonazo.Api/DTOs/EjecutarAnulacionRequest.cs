namespace ElCarbonazo.Api.DTOs;

public sealed record EjecutarAnulacionRequest(
    Guid IdOperacion, int IdComanda, int? IdDetalle, string Motivo, string Token);
