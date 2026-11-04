using System.Text.Json.Serialization;

namespace ElCarbonazo.Desktop.Models;

public sealed record MesaDto(int IdMesa, int? IdGrupo, int Numero, int Capacidad, string Estado, uint Version)
{
    [JsonIgnore]
    public string GrupoTexto => IdGrupo is int id ? $"Grupo {id}" : "Sin grupo";
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
