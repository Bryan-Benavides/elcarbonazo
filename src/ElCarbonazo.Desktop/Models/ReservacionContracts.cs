using System.Text.Json.Serialization;

namespace ElCarbonazo.Desktop.Models;

public sealed record CrearReservacionRequest(
    int IdMesa, string NombreReferencia, int CantidadPersonas, DateTimeOffset InicioUtc, DateTimeOffset FinUtc);

public sealed record ReservacionDto(
    int IdReservacion, int IdMesa, int NumeroMesa, string NombreReferencia, int CantidadPersonas,
    DateTimeOffset InicioUtc, DateTimeOffset FinUtc, string Estado, DateTimeOffset CreadaUtc)
{
    [JsonIgnore]
    public string InicioLocalTexto
    {
        get
        {
            foreach (var id in new[] { "America/Managua", "Central America Standard Time" })
            {
                try
                {
                    return TimeZoneInfo.ConvertTime(InicioUtc, TimeZoneInfo.FindSystemTimeZoneById(id))
                        .ToString("dd/MM/yyyy HH:mm");
                }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return InicioUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        }
    }
}

public sealed record DisponibilidadMesaDto(int IdMesa, int Numero, int Capacidad);
public sealed record ReservacionesPage(IReadOnlyList<ReservacionDto> Items);

public sealed record ReporteVentasDto(
    DateTimeOffset DesdeUtc,
    DateTimeOffset HastaUtc,
    int CantidadCobros,
    decimal TotalCobrado,
    decimal TicketPromedio,
    decimal ImpuestosRegistrados,
    IReadOnlyList<ReporteProductoDto> Productos);

public sealed record ReporteProductoDto(string NombreProducto, long UnidadesVendidas, decimal TotalVendido);
