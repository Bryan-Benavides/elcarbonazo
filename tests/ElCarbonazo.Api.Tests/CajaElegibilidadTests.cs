using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class CajaElegibilidadTests
{
    [Fact]
    public void Valid_historical_snapshot_has_stable_hash_and_detects_changes()
    {
        var order = ValidOrder();
        var first = CajaElegibilidad.Evaluar(order);
        Assert.True(first.Cobrable);
        Assert.Equal(64, first.HuellaLineas!.Length);
        order.Comandas = order.Comandas.Reverse().ToArray();
        Assert.Equal(first.HuellaLineas, CajaElegibilidad.Evaluar(order).HuellaLineas);
        order.Comandas.First().Version++;
        Assert.NotEqual(first.HuellaLineas, CajaElegibilidad.Evaluar(order).HuellaLineas);
    }

    [Fact]
    public void Blocks_legacy_pending_empty_zero_paid_and_inconsistent_accounts()
    {
        var order = ValidOrder();
        order.Comandas.Clear();
        Assert.Equal("CUENTA_LEGADA_SIN_COMANDAS", CajaElegibilidad.Evaluar(order).Motivo);
        order = ValidOrder();
        order.Comandas.First().Estado = "Abierta";
        Assert.Equal("COMANDAS_PENDIENTES", CajaElegibilidad.Evaluar(order).Motivo);
        order = ValidOrder();
        order.Comandas.First().Detalles.Clear();
        Assert.Equal("CUENTA_SIN_DETALLE", CajaElegibilidad.Evaluar(order).Motivo);
        order = ValidOrder();
        order.Total = 0;
        Assert.Equal("TOTAL_NO_COBRABLE", CajaElegibilidad.Evaluar(order).Motivo);
        order = ValidOrder();
        order.Estado = PedidoStates.Pagado;
        Assert.Equal("CUENTA_YA_PAGADA", CajaElegibilidad.Evaluar(order).Motivo);
        order = ValidOrder();
        order.Subtotal = 21;
        Assert.Equal("TOTAL_INCONSISTENTE", CajaElegibilidad.Evaluar(order).Motivo);
        order = ValidOrder();
        order.Comandas.First().Detalles.First().Subtotal = 9;
        Assert.Equal("DETALLE_INCONSISTENTE", CajaElegibilidad.Evaluar(order).Motivo);
    }

    private static Pedido ValidOrder() => new()
    {
        IdPedido = 1,
        IdMesa = 1,
        Estado = PedidoStates.Listo,
        Subtotal = 20,
        Total = 20,
        Comandas = new List<Comanda>
        {
            new() { IdComanda = 1, IdPedido = 1, Estado = "Lista", Version = 1, Detalles = new List<DetallePedido>
                { new() { IdDetalle = 1, IdComanda = 1, IdProducto = 2, NombreProducto = "Producto", Cantidad = 1, PrecioUnitario = 10, Subtotal = 10 } } },
            new() { IdComanda = 2, IdPedido = 1, Estado = "Lista", Version = 2, Detalles = new List<DetallePedido>
                { new() { IdDetalle = 2, IdComanda = 2, IdProducto = 3, NombreProducto = "Otro", Cantidad = 1, PrecioUnitario = 10, Subtotal = 10 } } }
        }
    };
}
