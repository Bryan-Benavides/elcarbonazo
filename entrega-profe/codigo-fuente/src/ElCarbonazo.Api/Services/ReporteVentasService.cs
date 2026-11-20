using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed class ReporteVentasService(AppDbContext context)
{
    public async Task<(int Status, string Code, ReporteVentasDto? Reporte)> ObtenerAsync(
        DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
    {
        if (desde.Offset != TimeSpan.Zero || hasta.Offset != TimeSpan.Zero ||
            desde >= hasta || hasta - desde > TimeSpan.FromDays(366))
            return (400, "VALIDATION_ERROR", null);

        var charges = context.Cobros.AsNoTracking()
            .Where(x => x.FechaHora >= desde && x.FechaHora < hasta);
        var summary = await charges.GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.Count(),
                Total = group.Sum(x => x.Total),
                Taxes = group.Sum(x => x.Impuestos)
            })
            .SingleOrDefaultAsync(ct);
        var count = summary?.Count ?? 0;
        var total = summary?.Total ?? 0m;
        var taxes = summary?.Taxes ?? 0m;
        var products = await context.ComprobantesCobroLineas.AsNoTracking()
            .Where(line => line.Cobro.FechaHora >= desde && line.Cobro.FechaHora < hasta)
            .GroupBy(line => line.NombreProducto)
            .Select(group => new
            {
                Name = group.Key,
                Units = group.Sum(line => (long)line.Cantidad),
                Sales = group.Sum(line => line.Subtotal)
            })
            .OrderByDescending(x => x.Units).ThenByDescending(x => x.Sales).ThenBy(x => x.Name)
            .Take(10)
            .Select(x => new ReporteProductoDto(x.Name, x.Units, x.Sales))
            .ToListAsync(ct);
        return (200, "OK", new ReporteVentasDto(desde, hasta, count, total,
            count == 0 ? 0m : decimal.Round(total / count, 2, MidpointRounding.AwayFromZero), taxes, products));
    }
}
