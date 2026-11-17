namespace ElCarbonazo.Api.DTOs;

public sealed record CrearReservacionRequest(
    int IdMesa,
    string NombreReferencia,
    int CantidadPersonas,
    DateTimeOffset InicioUtc,
    DateTimeOffset FinUtc);

public sealed record ReservacionDto(
    int IdReservacion,
    int IdMesa,
    int NumeroMesa,
    string NombreReferencia,
    int CantidadPersonas,
    DateTimeOffset InicioUtc,
    DateTimeOffset FinUtc,
    string Estado,
    DateTimeOffset CreadaUtc);

public sealed record DisponibilidadMesaDto(int IdMesa, int Numero, int Capacidad);
public sealed record ProximaReservacionDto(DateTimeOffset InicioUtc, DateTimeOffset FinUtc, int CantidadPersonas);

public sealed record ReservacionesError(int Status, string Code, string? Message = null, ReservacionDto? Reservacion = null,
    IReadOnlyList<DisponibilidadMesaDto>? Disponibles = null);
