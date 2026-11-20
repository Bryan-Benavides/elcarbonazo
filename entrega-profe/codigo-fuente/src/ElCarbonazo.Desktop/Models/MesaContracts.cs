using System.Text.Json.Serialization;

namespace ElCarbonazo.Desktop.Models;

public sealed record MesaDto(int IdMesa, int? IdGrupo, int Numero, int Capacidad, string Estado, uint Version,
    ProximaReservacionDto? ProximaReservacion = null)
{
    [JsonIgnore]
    public string GrupoTexto => IdGrupo is int id ? $"Grupo {id}" : "Sin grupo";
}

public sealed record ProximaReservacionDto(DateTimeOffset InicioUtc, DateTimeOffset FinUtc, int CantidadPersonas)
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
                        .ToString("dd/MM HH:mm");
                }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return InicioUtc.ToLocalTime().ToString("dd/MM HH:mm");
        }
    }
}

public sealed record UpdateMesaEstadoRequest(string Estado, uint Version);

public sealed record MesaVersionRequest(int IdMesa, uint Version);

public sealed record CreateGrupoMesaRequest(IReadOnlyList<MesaVersionRequest> Mesas);

public sealed record GrupoMesaDto(
    int IdGrupo,
    int IdUsuarioCreacion,
    DateTimeOffset FechaCreacion,
    bool Activo,
    IReadOnlyList<MesaDto> Mesas);
