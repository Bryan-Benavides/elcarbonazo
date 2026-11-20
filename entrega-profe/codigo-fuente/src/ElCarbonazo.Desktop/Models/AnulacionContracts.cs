namespace ElCarbonazo.Desktop.Models;

public sealed record AutorizarAnulacionRequest(
    Guid IdOperacion, int IdPedido, int IdComanda, int? IdDetalle, string Motivo, string EmailJefa, string Contrasena);

public sealed record AutorizarAnulacionResponse(string Token, DateTimeOffset ExpiraUtc);

public sealed record EjecutarAnulacionRequest(
    Guid IdOperacion, int IdComanda, int? IdDetalle, string Motivo, string Token);

public sealed record ResultadoAnulacionDto(
    string Estado,
    string Codigo,
    long? IdAnulacion = null,
    decimal? MontoRevertido = null,
    decimal? Subtotal = null,
    decimal? Total = null,
    int? DetalleId = null);

public sealed record OperacionAnulacionDto(string Estado, string Codigo, string? Resultado);

/// <summary>Intención inmutable en memoria; nunca se persiste con motivo ni credenciales.</summary>
public sealed record IntencionAnulacion(
    Guid IdOperacion, int IdPedido, int IdComanda, int? IdDetalle, string Motivo);
