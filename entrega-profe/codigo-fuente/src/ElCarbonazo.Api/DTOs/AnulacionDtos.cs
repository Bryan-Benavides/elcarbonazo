namespace ElCarbonazo.Api.DTOs;

public sealed record AutorizarAnulacionRequest(
    Guid IdOperacion, int IdPedido, int IdComanda, int? IdDetalle, string Motivo, string EmailJefa, string Contrasena);

public sealed record AutorizarAnulacionResponse(string Token, DateTimeOffset ExpiraUtc);
