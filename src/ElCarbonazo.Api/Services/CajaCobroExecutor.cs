using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using ElCarbonazo.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed class CajaCobroExecutor(AppDbContext context, IAuditRecorder audit, TimeProvider time) : ICajaEjecutorDominio
{
    public bool PuedeEjecutar(string tipo) => tipo == "CobrarCuenta";

    public async Task<CajaResultadoDominio> EjecutarAsync(CajaEjecucionPreparada operacion, CancellationToken cancellationToken)
    {
        if (operacion.Tipo != "CobrarCuenta" || operacion.Version != 1) throw new InvalidOperationException("Contrato no soportado.");
        using var parsed = JsonDocument.Parse(operacion.ArgumentosJson);
        var args = parsed.RootElement;
        var orderId = args.GetProperty("pedidoId").GetInt32();
        var shiftId = args.GetProperty("turnoId").GetInt32();
        var shownTotal = args.GetProperty("totalVisto").GetDecimal();
        var fingerprint = args.GetProperty("huellaLineas").GetString();
        var method = args.GetProperty("metodo").GetString();
        var cashValue = args.GetProperty("efectivoRecibido");
        decimal? cash = cashValue.ValueKind == JsonValueKind.Null ? null : cashValue.GetDecimal();
        var referenceValue = args.GetProperty("referencia");
        var reference = referenceValue.ValueKind == JsonValueKind.Null ? null : referenceValue.GetString();
        var verified = args.GetProperty("verificacionExterna").GetBoolean();
        var repeated = args.GetProperty("referenciaRepetida").GetBoolean();
        // Reconstruct through the canonical validator to reject invalid or tampered durable arguments.
        if (CajaOperacionCanonica.Cobrar(orderId, shiftId, shownTotal, fingerprint, method, cash, reference, verified, repeated) is null)
            throw new InvalidOperationException("La operación preparada no contiene un cobro válido.");

        var order = await CajaCuentaLock.ObtenerAsync(context, orderId, cancellationToken);
        if (order is null) return Reject("CUENTA_NO_COBRABLE");
        var eligibility = CajaElegibilidad.Evaluar(order);
        if (!eligibility.Cobrable) return Reject(order.Estado == PedidoStates.Pagado ? "CUENTA_YA_PAGADA" : "CUENTA_NO_COBRABLE");
        if (order.Total != shownTotal || eligibility.HuellaLineas != fingerprint) return Reject("CUENTA_CAMBIO_REQUIERE_REVISION");
        if (await CajaPendientes.HayCobroParaCuentaAsync(context, orderId, operacion.IdOperacion, cancellationToken)) return Reject("OPERACION_NO_TERMINAL");

        var shift = await context.TurnosCaja.FromSqlInterpolated($"SELECT * FROM turno_caja WHERE id_turno_caja = {shiftId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        await RevalidarAutorizacionAsync(operacion, cancellationToken);
        if (shift is null || shift.Estado != CajaStates.TurnoAbierto) return Reject("TURNO_NO_ABIERTO");
        if (method == CajaStates.Efectivo && cash < order.Total)
            return new(false, 400, "VALIDATION_ERROR", "{\"estado\":\"Rechazada\",\"codigo\":\"VALIDATION_ERROR\"}");
        if (reference is not null)
        {
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"elcarbonazo:caja:referencia:v1:{method}:{reference}"));
            var key = BinaryPrimitives.ReadInt64BigEndian(digest);
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken);
            await RevalidarAutorizacionAsync(operacion, cancellationToken);
            var duplicate = await context.Cobros.AsNoTracking().AnyAsync(x => x.MetodoPago == method && x.ReferenciaExterna == reference, cancellationToken);
            if (duplicate && !repeated) return Reject("REFERENCIA_REQUIERE_CONFIRMACION");
        }

        var now = time.GetUtcNow();
        var payment = new Cobro
        {
            IdPedido = orderId,
            IdTurnoCaja = shiftId,
            IdUsuario = operacion.IdUsuario,
            IdOperacion = operacion.IdOperacion,
            MetodoPago = method!,
            Subtotal = order.Subtotal,
            Impuestos = order.Impuestos,
            Total = order.Total,
            EfectivoRecibido = cash,
            Vuelto = cash is null ? null : cash.Value - order.Total,
            ReferenciaExterna = reference,
            VerificacionExternaConfirmada = verified,
            ReferenciaRepetidaConfirmada = repeated,
            FechaHora = now
        };
        var position = 0;
        foreach (var detail in order.Comandas.OrderBy(x => x.IdComanda).SelectMany(x => x.Detalles.OrderBy(d => d.IdDetalle)))
            payment.Lineas.Add(new ComprobanteCobroLinea
            {
                Orden = ++position,
                IdDetalleOrigen = detail.IdDetalle,
                NombreProducto = detail.NombreProducto,
                Cantidad = detail.Cantidad,
                PrecioUnitario = detail.PrecioUnitario,
                Subtotal = detail.Subtotal
            });
        order.Estado = PedidoStates.Pagado;
        context.Cobros.Add(payment);
        await context.SaveChangesAsync(cancellationToken);
        audit.Record("CobroRegistrado", "Exitoso", operacion.IdUsuario, entityType: "Cobro",
            entityId: payment.IdCobro.ToString(CultureInfo.InvariantCulture), details: new Dictionary<string, object?>
            { ["idPedido"] = orderId, ["idTurnoCaja"] = shiftId, ["idOperacion"] = operacion.IdOperacion, ["metodo"] = method, ["total"] = order.Total });
        var response = JsonSerializer.Serialize(new
        {
            estado = "Completada",
            codigo = "COBRO_REGISTRADO",
            idCobro = payment.IdCobro,
            numeroComprobante = payment.NumeroComprobante,
            idPedido = orderId,
            idTurnoCaja = shiftId,
            total = order.Total,
            metodoPago = method,
            efectivoRecibido = cash,
            vuelto = payment.Vuelto,
            fechaHora = now
        });
        return new(true, 201, "COBRO_REGISTRADO", response);
    }

    private static CajaResultadoDominio Reject(string code) =>
        new(false, 409, code, JsonSerializer.Serialize(new { estado = "Rechazada", codigo = code }));

    private async Task RevalidarAutorizacionAsync(CajaEjecucionPreparada operation, CancellationToken cancellationToken)
    {
        if (operation.IdSesion is Guid session && !await CajaSesion.VigenteAsync(context, session, operation.IdUsuario, time.GetUtcNow(), cancellationToken))
            throw new CajaAutorizacionRevocadaException(401, "AUTH_SESION_EXPIRADA");
        if (!await context.Usuarios.AsNoTracking().AnyAsync(x => x.IdUsuario == operation.IdUsuario && x.Activo &&
                (x.Rol == UserRoles.Jefa || x.Rol == UserRoles.EncargadoCaja), cancellationToken))
            throw new CajaAutorizacionRevocadaException(403, "AUTH_PROHIBIDO");
    }
}
