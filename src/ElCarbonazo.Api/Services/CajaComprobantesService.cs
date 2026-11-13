using System.Data;
using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed class CajaComprobantesService(AppDbContext context)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CajaComprobantePaginaDto?> ListarAsync(string? cursor, int limite, CancellationToken cancellationToken)
    {
        if (limite is < 1 or > 100) return null;
        long last = 0;
        if (cursor is not null)
        {
            try
            {
                if (cursor.Length is 0 or > 256 || cursor.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return null;
                var encoded = cursor.Replace('-', '+').Replace('_', '/');
                encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
                var decoded = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(encoded), JsonOptions);
                if (decoded is null || decoded.Tipo != "caja-comprobantes-id-v1" || decoded.Id <= 0) return null;
                last = decoded.Id;
            }
            catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException) { return null; }
        }
        var items = await context.Cobros.AsNoTracking().Where(x => x.IdCobro > last).OrderBy(x => x.IdCobro)
            .Take(limite + 1).Select(x => new CajaComprobanteItemDto(x.IdCobro, x.NumeroComprobante, x.IdPedido,
                x.IdTurnoCaja, x.IdUsuario, x.MetodoPago, x.Total, x.FechaHora)).ToListAsync(cancellationToken);
        var more = items.Count > limite;
        if (more) items.RemoveAt(items.Count - 1);
        var next = more ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Cursor("caja-comprobantes-id-v1", items[^1].IdCobro), JsonOptions))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_') : null;
        return new CajaComprobantePaginaDto(items, next);
    }

    public async Task<CajaComprobanteDto?> ObtenerAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0) return null;
        await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var payment = await context.Cobros.AsNoTracking().AsSplitQuery().Where(x => x.IdCobro == id)
            .Include(x => x.Lineas)
            .Include(x => x.Pedido).ThenInclude(x => x.Mesa)
            .Include(x => x.Pedido).ThenInclude(x => x.Grupo).ThenInclude(x => x!.Miembros).ThenInclude(x => x.Mesa)
            .SingleOrDefaultAsync(cancellationToken);
        if (payment is null) return null;
        var dto = new CajaComprobanteDto("COMPROBANTE INTERNO — DEMOSTRACIÓN — NO ES FACTURA FISCAL",
            payment.IdCobro, payment.NumeroComprobante, payment.IdOperacion, payment.IdPedido, payment.IdTurnoCaja,
            payment.IdUsuario, payment.Pedido.Mesa?.Numero, payment.Pedido.IdGrupo,
            payment.Pedido.Grupo?.Miembros.OrderBy(x => x.Mesa.Numero).Select(x => x.Mesa.Numero).ToArray() ?? [],
            payment.FechaHora, payment.MetodoPago, payment.Subtotal, payment.Impuestos, payment.Total,
            payment.EfectivoRecibido, payment.Vuelto, payment.ReferenciaExterna,
            payment.Lineas.OrderBy(x => x.Orden).Select(x => new CajaComprobanteLineaDto(x.Orden, x.IdDetalleOrigen,
                x.NombreProducto, x.Cantidad, x.PrecioUnitario, x.Subtotal)).ToArray());
        await tx.CommitAsync(cancellationToken);
        return dto;
    }

    private sealed record Cursor(string Tipo, long Id);
}
