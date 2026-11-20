namespace ElCarbonazo.Api.DTOs;

public sealed record ReporteVentasDto(
    DateTimeOffset DesdeUtc,
    DateTimeOffset HastaUtc,
    int CantidadCobros,
    decimal TotalCobrado,
    decimal TicketPromedio,
    decimal ImpuestosRegistrados,
    IReadOnlyList<ReporteProductoDto> Productos);

public sealed record ReporteProductoDto(string NombreProducto, long UnidadesVendidas, decimal TotalVendido);
