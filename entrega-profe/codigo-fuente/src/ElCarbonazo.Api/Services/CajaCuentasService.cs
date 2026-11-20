using System.Data;
using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed class CajaCuentasService(AppDbContext context)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CajaCuentasPaginaDto?> ListarAsync(string? cursor, int limite, CancellationToken cancellationToken)
    {
        if (limite is < 1 or > 100) return null;
        var last = 0;
        if (cursor is not null)
        {
            try
            {
                if (cursor.Length is 0 or > 256 || cursor.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return null;
                var encoded = cursor.Replace('-', '+').Replace('_', '/');
                encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
                var decoded = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(encoded), JsonOptions);
                if (decoded is null || decoded.Tipo != "caja-cuentas-id-v1" || decoded.Id <= 0) return null;
                last = decoded.Id;
            }
            catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException) { return null; }
        }
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var rows = await context.Pedidos.AsNoTracking().AsSplitQuery()
            .Where(x => x.IdPedido > last)
            .OrderBy(x => x.IdPedido).Take(limite + 1)
            .Include(x => x.Mesa).Include(x => x.Grupo).ThenInclude(x => x!.MesasActivas)
            .Include(x => x.Comandas).ThenInclude(x => x.Detalles)
            .ToListAsync(cancellationToken);
        var more = rows.Count > limite;
        if (more) rows.RemoveAt(rows.Count - 1);
        var pending = await context.OperacionesCaja.AsNoTracking()
            .Where(x => x.Tipo == "CobrarCuenta" && x.Estado == CajaStates.OperacionPreparada)
            .Select(x => x.Argumentos).ToListAsync(cancellationToken);
        var blockedOrders = new HashSet<int>();
        var unknownPending = false;
        foreach (var arguments in pending)
        {
            try
            {
                if (arguments is null) { unknownPending = true; break; }
                using var parsed = JsonDocument.Parse(arguments);
                if (!parsed.RootElement.TryGetProperty("pedidoId", out var value) || !value.TryGetInt32(out var id) || id <= 0)
                { unknownPending = true; break; }
                blockedOrders.Add(id);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { unknownPending = true; break; }
        }
        var result = rows.Select(order => ToDto(order, unknownPending || blockedOrders.Contains(order.IdPedido))).ToArray();
        var next = more ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Cursor("caja-cuentas-id-v1", rows[^1].IdPedido), JsonOptions))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_') : null;
        await transaction.CommitAsync(cancellationToken);
        return new CajaCuentasPaginaDto(result, next);
    }

    private static CajaCuentaDto ToDto(Pedido order, bool paymentPending)
    {
        var eligibility = CajaElegibilidad.Evaluar(order);
        var members = order.Grupo?.MesasActivas.OrderBy(x => x.Numero).ToArray() ?? [];
        var destinationValid = order.IdMesa is not null
            ? order.Mesa is { Estado: MesaStates.Ocupada, IdGrupo: null }
            : order.Grupo is { Activo: true } && members.Length >= 2 && members.All(x => x.Estado == MesaStates.Agrupada);
        if (!destinationValid && eligibility.Cobrable) eligibility = new CajaElegibilidadResult(false, "DESTINO_INVALIDO", null);
        if (paymentPending && eligibility.Cobrable) eligibility = new CajaElegibilidadResult(false, "OPERACION_NO_TERMINAL", null);
        var commands = order.Comandas.OrderBy(x => x.IdComanda).Select(c => new CajaComandaDto(c.IdComanda, c.Estado, c.Version,
            c.Detalles.OrderBy(d => d.IdDetalle).Select(d => new CajaLineaDto(d.IdDetalle, d.IdProducto, d.NombreProducto,
                d.Cantidad, d.PrecioUnitario, d.Subtotal)).ToArray())).ToArray();
        return new CajaCuentaDto(order.IdPedido, order.IdMesa, order.Mesa?.Numero, order.IdGrupo, members.Select(x => x.Numero).ToArray(),
            order.Estado, order.Subtotal, order.Impuestos, order.Total, eligibility.Cobrable, eligibility.Motivo, eligibility.HuellaLineas, commands);
    }

    private sealed record Cursor(string Tipo, int Id);
}
