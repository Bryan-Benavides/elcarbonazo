using System.Globalization;
using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Services;

public interface IReporteVentasApiService
{
    Task<ReporteVentasDto> ObtenerAsync(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct);
}

public sealed class ReporteVentasApiService(ISessionManager session) : IReporteVentasApiService
{
    public async Task<ReporteVentasDto> ObtenerAsync(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
    {
        var path = $"api/reportes/ventas?desdeUtc={Q(desde)}&hastaUtc={Q(hasta)}";
        using var response = await session.SendHumanAsync(() => new HttpRequestMessage(HttpMethod.Get, path), true, ct);
        var result = await MesaApiResponse.ReadAsync<ReporteVentasDto>(response, ct);
        if (result.DesdeUtc != desde || result.HastaUtc != hasta || result.CantidadCobros < 0 ||
            result.TotalCobrado < 0 || result.TicketPromedio < 0 || result.ImpuestosRegistrados < 0 ||
            result.Productos is null || result.Productos.Count > 10 ||
            result.Productos.Any(x => string.IsNullOrWhiteSpace(x.NombreProducto) || x.UnidadesVendidas <= 0 || x.TotalVendido < 0))
            throw new InvalidDataException("La API devolvió un reporte de ventas inválido.");
        return result;
    }

    private static string Q(DateTimeOffset value) => Uri.EscapeDataString(value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
}
