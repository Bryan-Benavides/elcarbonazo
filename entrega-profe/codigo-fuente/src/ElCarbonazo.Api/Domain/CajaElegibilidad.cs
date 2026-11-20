using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElCarbonazo.Api.Entities;

namespace ElCarbonazo.Api.Domain;

public sealed record CajaElegibilidadResult(bool Cobrable, string? Motivo, string? HuellaLineas);

// The caller must load the complete command/detail snapshot and, for writes, hold the approved locks.
public static class CajaElegibilidad
{
    public static CajaElegibilidadResult Evaluar(Pedido pedido)
    {
        if (pedido.IdPedido <= 0 || (pedido.IdMesa is null) == (pedido.IdGrupo is null) ||
            pedido.IdMesa is <= 0 || pedido.IdGrupo is <= 0) return Bloqueada("DESTINO_INVALIDO");
        if (pedido.Estado == PedidoStates.Pagado) return Bloqueada("CUENTA_YA_PAGADA");
        if (pedido.Estado == PedidoStates.Cancelado) return Bloqueada("CUENTA_CANCELADA");
        if (!PedidoStates.Active.Contains(pedido.Estado)) return Bloqueada("ESTADO_INVALIDO");
        var allCommands = pedido.Comandas.OrderBy(x => x.IdComanda).ToArray();
        if (allCommands.Length == 0) return Bloqueada("CUENTA_LEGADA_SIN_COMANDAS");
        if (allCommands.Any(x => x.IdComanda <= 0 || x.IdPedido != pedido.IdPedido) ||
            allCommands.Select(x => x.IdComanda).Distinct().Count() != allCommands.Length) return Bloqueada("DETALLE_INCONSISTENTE");
        if (allCommands.Any(x => x.Estado is not ("Abierta" or "En Preparación" or "Lista" or "Cancelada"))) return Bloqueada("ESTADO_INVALIDO");
        var commands = allCommands.Where(x => x.Estado != "Cancelada").ToArray();
        if (commands.Length == 0) return Bloqueada("CUENTA_CANCELADA");
        if (pedido.Estado != PedidoStates.Listo || commands.Any(x => x.Estado != "Lista")) return Bloqueada("COMANDAS_PENDIENTES");
        if (commands.Any(x => x.Detalles.Count == 0)) return Bloqueada("CUENTA_SIN_DETALLE");

        var details = commands.SelectMany(x => x.Detalles.Where(d => !d.Cancelado).OrderBy(d => d.IdDetalle)).ToArray();
        if (details.Select(x => x.IdDetalle).Distinct().Count() != details.Length) return Bloqueada("DETALLE_INCONSISTENTE");
        if (details.Length == 0) return Bloqueada("CUENTA_SIN_DETALLE");
        decimal subtotal = 0;
        try
        {
            foreach (var command in commands)
                foreach (var detail in command.Detalles.Where(d => !d.Cancelado))
                {
                    if (detail.IdDetalle <= 0 || detail.IdComanda != command.IdComanda || detail.IdProducto <= 0 ||
                        string.IsNullOrEmpty(detail.NombreProducto) || detail.NombreProducto.EnumerateRunes().Count() > 120 ||
                        !detail.NombreProducto.IsNormalized(NormalizationForm.FormC) || detail.Cantidad is < 1 or > 99 ||
                        detail.PrecioUnitario is < 0.01m or > 999999.99m || decimal.Round(detail.PrecioUnitario, 2) != detail.PrecioUnitario ||
                        detail.Subtotal != detail.Cantidad * detail.PrecioUnitario)
                        return Bloqueada("DETALLE_INCONSISTENTE");
                    subtotal = checked(subtotal + detail.Subtotal);
                }
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentException) { return Bloqueada("DETALLE_INCONSISTENTE"); }
        if (pedido.Total <= 0) return Bloqueada("TOTAL_NO_COBRABLE");
        if (pedido.Impuestos != 0 || pedido.Subtotal != subtotal || pedido.Total != subtotal || subtotal > 9999999999.99m)
            return Bloqueada("TOTAL_INCONSISTENTE");

        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            pedido = pedido.IdPedido,
            mesa = pedido.IdMesa,
            grupo = pedido.IdGrupo,
            total = Amount(pedido.Total),
            comandas = commands.Select(c => new
            {
                id = c.IdComanda,
                version = c.Version,
                estado = c.Estado,
                lineas = c.Detalles.Where(d => !d.Cancelado).OrderBy(d => d.IdDetalle).Select(d => new
                {
                    id = d.IdDetalle,
                    producto = d.IdProducto,
                    nombre = d.NombreProducto,
                    cantidad = d.Cantidad,
                    precio = Amount(d.PrecioUnitario),
                    subtotal = Amount(d.Subtotal)
                }).ToArray()
            }).ToArray()
        });
        return new(true, null, Convert.ToHexStringLower(SHA256.HashData(canonical)));
    }

    private static string Amount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
    private static CajaElegibilidadResult Bloqueada(string reason) => new(false, reason, null);
}
