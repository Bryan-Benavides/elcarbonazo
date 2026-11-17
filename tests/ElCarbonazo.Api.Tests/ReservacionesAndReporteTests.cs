using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.DTOs;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using ElCarbonazo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ElCarbonazo.Api.Tests;

public sealed class ReservacionesAndReporteTests
{
    [Fact]
    public async Task Report_aggregates_historical_receipts_with_half_open_boundaries_and_top_ten()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var until = from.AddDays(1);
            await using (var setup = database.CreateContext())
            {
                var audit = new AuditRecorder(setup, new TestHttpContextAccessor(), TimeProvider.System);
                var caja = new CajaOperacionesService(setup, audit, TimeProvider.System,
                    new CajaAperturaExecutor(setup, audit, TimeProvider.System));
                var opening = Guid.NewGuid();
                await caja.PrepararAsync(opening, 1, CajaOperacionCanonica.Abrir(0, true), default);
                Assert.Equal(201, (await caja.EjecutarAsync(opening, 1, default)).Status);
                var shift = await setup.TurnosCaja.Select(x => x.IdTurnoCaja).SingleAsync();
                var product = new Producto { Nombre = "Nombre original", Categoria = "Comida", Precio = 10, Disponible = true };
                setup.Productos.Add(product);
                await setup.SaveChangesAsync();
                foreach (var date in new[] { from.AddTicks(-10), from, until.AddTicks(-10), until })
                {
                    var order = new Pedido { IdMesa = 1, IdUsuario = 1, FechaHora = date, Subtotal = 120m, Total = 120m, Estado = PedidoStates.Pagado };
                    var operation = new OperacionCaja
                    {
                        IdOperacion = Guid.NewGuid(),
                        IdUsuarioPropietario = 1,
                        Tipo = "CobrarCuenta",
                        VersionContrato = 1,
                        Argumentos = "{}",
                        HashArgumentos = new string('a', 64),
                        Estado = CajaStates.OperacionCompletada,
                        FechaCreacion = date,
                        FechaTerminal = date,
                        StatusResultado = 200,
                        CodigoResultado = "OK",
                        ResultadoJson = "{}"
                    };
                    setup.Pedidos.Add(order);
                    setup.OperacionesCaja.Add(operation);
                    await setup.SaveChangesAsync();
                    var charge = new Cobro
                    {
                        IdPedido = order.IdPedido,
                        IdTurnoCaja = shift,
                        IdUsuario = 1,
                        IdOperacion = operation.IdOperacion,
                        MetodoPago = CajaStates.Efectivo,
                        Subtotal = 120m,
                        Total = 120m,
                        EfectivoRecibido = 120m,
                        Vuelto = 0m,
                        FechaHora = date
                    };
                    for (var i = 0; i < 12; i++)
                        charge.Lineas.Add(new ComprobanteCobroLinea
                        {
                            Orden = i + 1,
                            IdDetalleOrigen = i + 1,
                            NombreProducto = i == 0 ? "Nombre original" : $"Producto {i:00}",
                            Cantidad = 1,
                            PrecioUnitario = 10m,
                            Subtotal = 10m
                        });
                    setup.Cobros.Add(charge);
                    await setup.SaveChangesAsync();
                }
                product.Nombre = "Nombre cambiado";
                await setup.SaveChangesAsync();
            }
            await using var context = database.CreateContext();
            var service = new ReporteVentasService(context);
            var result = await service.ObtenerAsync(from, until, default);
            Assert.Equal(200, result.Status);
            var report = Assert.IsType<ReporteVentasDto>(result.Reporte);
            Assert.Equal(2, report.CantidadCobros);
            Assert.Equal(240m, report.TotalCobrado);
            Assert.Equal(120m, report.TicketPromedio);
            Assert.Equal(0m, report.ImpuestosRegistrados);
            Assert.Equal(10, report.Productos.Count);
            Assert.All(report.Productos, row => Assert.Equal(2, row.UnidadesVendidas));
            Assert.Contains(report.Productos, row => row.NombreProducto == "Nombre original" && row.TotalVendido == 20m);
            Assert.DoesNotContain(report.Productos, row => row.NombreProducto == "Nombre cambiado");
            Assert.Equal(400, (await service.ObtenerAsync(until, from, default)).Status);
            Assert.Equal(400, (await service.ObtenerAsync(from, from.AddDays(367), default)).Status);
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Concurrent_reservations_for_same_table_allow_exactly_one_and_cancel_releases_slot()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            var start = DateTimeOffset.UtcNow.AddDays(2);
            await using (var seed = database.CreateContext())
            {
                await seed.Database.ExecuteSqlRawAsync("""
                    UPDATE mesa SET capacidad = 4, estado = 'Libre', id_grupo = NULL WHERE numero = 1;
                    """);
            }

            var request = new CrearReservacionRequest(1, "Defensa", 3, start, start.AddHours(2));
            var writes = await Task.WhenAll(SubmitAsync(database, request), SubmitAsync(database, request));
            Assert.Equal(1, writes.Count(x => x.Status == 201));
            Assert.Equal(1, writes.Count(x => x.Status == 409 && x.Code == "RESERVACION_CONFLICTO"));

            var id = writes.Single(x => x.Status == 201).Reservacion!.IdReservacion;
            var cancel = await CancelAsync(database, id);
            Assert.Equal(200, cancel.Status);
            Assert.Equal("Cancelada", cancel.Reservacion!.Estado);
            await using (var listContext = database.CreateContext())
            {
                var service = new ReservacionesService(listContext,
                    new AuditRecorder(listContext, new TestHttpContextAccessor(), TimeProvider.System), TimeProvider.System);
                var agenda = await service.ListarAsync(start.AddHours(-1), start.AddHours(3), CancellationToken.None);
                Assert.Empty(agenda.Items!);
                Assert.Equal("Cancelada", (await listContext.Reservaciones.SingleAsync()).Estado);
            }

            var second = await SubmitAsync(database, request);
            Assert.Equal(201, second.Status);
            await using var verify = database.CreateContext();
            Assert.Equal(2, await verify.Reservaciones.CountAsync());
            Assert.Equal(2, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "ReservacionCreada"));
            Assert.Equal(1, await verify.EventosAuditoria.CountAsync(x => x.TipoEvento == "ReservacionCancelada"));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task Availability_uses_half_open_ranges_and_checks_capacity()
    {
        var database = new TemporaryPostgresDatabase();
        try
        {
            await database.InitializeAsync();
            var start = DateTimeOffset.UtcNow.AddDays(2);
            await using (var seed = database.CreateContext())
            {
                await seed.Database.ExecuteSqlRawAsync("""
                    UPDATE mesa SET capacidad = CASE WHEN numero = 1 THEN 2 ELSE 5 END,
                        estado = 'Libre', id_grupo = NULL WHERE numero IN (1, 2);
                    """);
                await seed.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO reservacion
                        (id_mesa, nombre_referencia, cantidad_personas, inicio_utc, fin_utc, estado, creada_utc)
                    SELECT id_mesa, 'Anterior', 2, {start}, {start.AddHours(1)}, 'Activa', now()
                    FROM mesa WHERE numero = 2
                    """);
            }

            await using var context = database.CreateContext();
            var service = new ReservacionesService(context,
                new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System),
                TimeProvider.System);
            var end = start.AddHours(1);
            var result = await service.DisponibilidadAsync(end, end.AddHours(1), 3, CancellationToken.None);
            Assert.Equal(200, result.Status);
            Assert.Contains(result.Disponibles!, x => x.Numero == 2);
            Assert.DoesNotContain(result.Disponibles!, x => x.Numero == 1);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    private static async Task<ReservacionesError> SubmitAsync(
        TemporaryPostgresDatabase database, CrearReservacionRequest request)
    {
        await using var context = database.CreateContext();
        var service = new ReservacionesService(context,
            new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System),
            TimeProvider.System);
        return await service.CrearAsync(request, 1, CancellationToken.None);
    }

    private static async Task<ReservacionesError> CancelAsync(TemporaryPostgresDatabase database, int id)
    {
        await using var context = database.CreateContext();
        var service = new ReservacionesService(context,
            new AuditRecorder(context, new TestHttpContextAccessor(), TimeProvider.System),
            TimeProvider.System);
        return await service.CancelarAsync(id, 1, CancellationToken.None);
    }
}
