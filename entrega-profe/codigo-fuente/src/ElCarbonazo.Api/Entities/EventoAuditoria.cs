namespace ElCarbonazo.Api.Entities;

using System.Net;

public sealed class EventoAuditoria
{
    public long IdEvento { get; set; }
    public DateTimeOffset FechaHora { get; set; }
    public string TipoEvento { get; set; } = null!;
    public string Resultado { get; set; } = null!;
    public int? IdUsuario { get; set; }
    public Guid? IdSesion { get; set; }
    public IPAddress? DireccionIp { get; set; }
    public Guid? IdInstalacion { get; set; }
    public string? TipoEntidad { get; set; }
    public string? IdEntidad { get; set; }
    public string TraceId { get; set; } = null!;
    public string Detalles { get; set; } = "{}";
}
