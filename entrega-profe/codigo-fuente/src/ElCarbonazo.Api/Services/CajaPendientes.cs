using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

internal static class CajaPendientes
{
    public static async Task<bool> HayCobroParaCuentaAsync(AppDbContext context, int pedidoId, Guid except, CancellationToken cancellationToken)
    {
        var pending = await context.OperacionesCaja.AsNoTracking()
            .Where(x => x.IdOperacion != except && x.Tipo == "CobrarCuenta" && x.Estado == CajaStates.OperacionPreparada)
            .Select(x => x.Argumentos).ToListAsync(cancellationToken);
        foreach (var value in pending)
        {
            if (value is null) return true;
            try
            {
                using var json = JsonDocument.Parse(value);
                if (!json.RootElement.TryGetProperty("pedidoId", out var property) || !property.TryGetInt32(out var id) || id <= 0 || id == pedidoId)
                    return true;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return true; }
        }
        return false;
    }
}
