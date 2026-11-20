using System.Text.Json.Serialization;

namespace ElCarbonazo.Desktop.Models;

public sealed record CocinaLineaDto(int IdDetalle, string NombreProducto, short Cantidad, [property: JsonRequired] string Notas);

public sealed record CocinaComandaDto(
    int IdComanda, int IdPedido, int? IdMesa, int? NumeroMesa, int? IdGrupo,
    int IdUsuario, DateTimeOffset FechaHora, string Estado, [property: JsonRequired] uint Version,
    IReadOnlyList<CocinaLineaDto> Lineas)
{
    public string Destino => NumeroMesa is int numero ? $"Mesa {numero:00}" : $"Grupo {IdGrupo}";
    public string Accion => Estado == "Abierta" ? "Iniciar preparación" : "Marcar lista";
    public string Hora => FechaHora.UtcDateTime.ToString("dd/MM/yyyy HH:mm 'UTC'");
}

public sealed record CocinaPaginaDto(IReadOnlyList<CocinaComandaDto> Items, [property: JsonRequired] string? CursorSiguiente);
