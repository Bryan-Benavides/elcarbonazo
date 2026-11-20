using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;

namespace ElCarbonazo.Desktop.Services;

public sealed record CajaTurnoSnapshot(int IdTurnoCaja, string Estado, decimal FondoInicial, decimal TotalEfectivo,
    decimal TotalTarjeta, decimal TotalTransferencia, decimal EfectivoEsperado, int CantidadCuentasPendientes, int IdUsuarioApertura);
public sealed record CajaPagoLineaSnapshot(int Orden, string NombreProducto, int Cantidad, decimal PrecioUnitario, decimal Subtotal);
public sealed record CajaPagoSnapshot(long IdCobro, long NumeroComprobante, int IdPedido, int IdTurnoCaja,
    int IdUsuario, decimal Total, string MetodoPago, IReadOnlyList<CajaPagoLineaSnapshot> Lineas,
    decimal? EfectivoRecibido = null, decimal? Vuelto = null, DateTimeOffset? FechaHora = null,
    string? Destino = null, string? ReferenciaExterna = null);
// Recovery summary is informational; it must not replace a fresh, detailed account review before charging.
public sealed record CajaCuentaRecoverySnapshot(int IdPedido, string Estado, decimal Total, bool Cobrable, string? Motivo);

// The consuming view model must apply the validated snapshot before the journal can be cleared.
public interface ICajaRecoverySnapshotSink
{
    Task<bool> ApplyShiftAsync(CajaTurnoSnapshot snapshot, CancellationToken cancellationToken);
    Task<bool> ApplyPaymentAsync(CajaPagoSnapshot snapshot, CancellationToken cancellationToken);
    // Explicit null means that the API confirmed there is no open shift.
    Task<bool> ApplyCurrentShiftAsync(CajaTurnoSnapshot? snapshot, CancellationToken cancellationToken);
    Task<bool> ApplyAccountAndShiftAsync(CajaCuentaRecoverySnapshot account, CajaTurnoSnapshot shift, CancellationToken cancellationToken);
    // The consumer must discard all account/receipt and shift caches and require a fresh review before any write.
    Task<bool> ApplyBarrierAsync(string attemptedType, CajaTurnoSnapshot? currentShift, CancellationToken cancellationToken);
}

public interface ICajaTurnoActualReader
{
    Task<(bool Valid, CajaTurnoSnapshot? Snapshot)> ReadCurrentShiftAsync(int actor, CancellationToken cancellationToken);
}

// Delivered only after the same terminal rejection and business evidence have been validated.
// It contains no arguments or references and does not itself authorize a new payment.
public interface ICajaRejectionSnapshotSink
{
    Task<bool> ApplyPaymentRejectionAsync(string code, CajaCuentaRecoverySnapshot account, CajaTurnoSnapshot shift, CancellationToken cancellationToken);
}

public interface ICajaHistorySnapshotSink
{
    Task<bool> ApplyHistoryPaymentAsync(CajaPagoSnapshot snapshot, CancellationToken cancellationToken);
}

public sealed record CajaComprobanteResumen(long IdCobro, long NumeroComprobante, int IdPedido, string MetodoPago, decimal Total);
public sealed record CajaComprobantePagina(IReadOnlyList<CajaComprobanteResumen> Items, string? CursorSiguiente);
public interface ICajaComprobantesReader
{
    Task<CajaComprobantePagina?> ReadPageAsync(int actor, string? cursor, CancellationToken ct);
    Task<bool> ApplyDetailAsync(int actor, long id, CancellationToken ct);
}

public sealed class CajaRecoveryHttpTransport(ICajaSessionSender session, ICajaRecoverySnapshotSink? sink = null) : ICajaRecoveryTransport, ICajaTurnoActualReader, ICajaComprobantesReader
{
    public async Task<CajaComprobantePagina?> ReadPageAsync(int actor, string? cursor, CancellationToken ct)
    {
        if (actor <= 0 || (cursor is not null && !ValidCursor(cursor))) return null;
        var path = "api/caja/comprobantes?limite=25" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
        using var response = await session.SendCajaOnceAsync(actor, () => new HttpRequestMessage(HttpMethod.Get, path), true, ct);
        if (!response.IsSuccessStatusCode) return null;
        using var document = await ReadObjectAsync(response, ct, 65536);
        if (document is null || !document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 25 ||
            !document.RootElement.TryGetProperty("cursorSiguiente", out var next) || next.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) return null;
        var nextCursor = next.ValueKind == JsonValueKind.Null ? null : next.GetString();
        if (nextCursor is not null && (!ValidCursor(nextCursor) || nextCursor == cursor || items.GetArrayLength() == 0)) return null;
        var result = new List<CajaComprobanteResumen>();
        long previous = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != item.EnumerateObject().Count() ||
                !TryLong(item, "idCobro", out var id) || id <= previous || !TryLong(item, "numeroComprobante", out var number) || number <= 0 ||
                !TryInt(item, "idPedido", out var order) || order <= 0 || !TryInt(item, "idTurnoCaja", out var shift) || shift <= 0 ||
                !TryInt(item, "idUsuario", out var owner) || owner <= 0 || !TryString(item, "metodoPago", out var method) || method is not ("Efectivo" or "Tarjeta" or "Transferencia") ||
                !TryMoney(item, "total", out var total) || total <= 0 || !TryString(item, "fechaHora", out var date) ||
                !DateTimeOffset.TryParse(date, out var timestamp) || timestamp.Offset != TimeSpan.Zero) return null;
            result.Add(new CajaComprobanteResumen(id, number, order, method!, total));
            previous = id;
        }
        return new CajaComprobantePagina(result, nextCursor);
    }

    public async Task<bool> ApplyDetailAsync(int actor, long id, CancellationToken ct)
    {
        if (actor <= 0 || id <= 0 || sink is null) return false;
        using var response = await session.SendCajaOnceAsync(actor,
            () => new HttpRequestMessage(HttpMethod.Get, $"api/caja/comprobantes/{id}"), true, ct);
        if (!response.IsSuccessStatusCode) return false;
        using var receipt = await ReadObjectAsync(response, ct, 1048576);
        if (receipt is null) return false;
        var root = receipt.RootElement;
        if (!TryLong(root, "idCobro", out var payment) || payment != id || !TryLong(root, "numeroComprobante", out var number) ||
            !TryInt(root, "idPedido", out var order) || !TryInt(root, "idTurnoCaja", out var shift) ||
            !TryMoney(root, "total", out var total) || !TryString(root, "metodoPago", out var method) ||
            !TryString(root, "idOperacion", out var operationText) || !Guid.TryParse(operationText, out var operation) || operation == Guid.Empty) return false;
        decimal? cash = null, change = null;
        if (method == "Efectivo")
        {
            if (!TryMoney(root, "efectivoRecibido", out var received) || !TryMoney(root, "vuelto", out var returned)) return false;
            cash = received;
            change = returned;
        }
        var terminal = JsonSerializer.SerializeToElement(new
        {
            codigo = "COBRO_REGISTRADO",
            idCobro = payment,
            numeroComprobante = number,
            idPedido = order,
            idTurnoCaja = shift,
            total,
            metodoPago = method,
            efectivoRecibido = cash,
            vuelto = change
        });
        return await ApplyPaymentReceiptAsync(operation, terminal, root, ct, history: true);
    }

    private static bool ValidCursor(string value) => value.Length is > 0 and <= 256 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public async Task<CajaRecoveryOutcome> PrepareAsync(Guid operation, int actor, string type, object? arguments, CancellationToken cancellationToken)
    {
        if (arguments is null) return CajaRecoveryOutcome.Uncertain;
        using var response = await session.SendCajaOnceAsync(actor, () => new HttpRequestMessage(HttpMethod.Put, $"api/caja/operaciones/{operation:D}")
        {
            Content = JsonContent.Create(new { tipo = type, version = 1, argumentos = arguments })
        }, false, cancellationToken);
        return await ParseAsync(response, cancellationToken);
    }

    public async Task<CajaRecoveryOutcome> QueryAsync(Guid operation, int actor, CancellationToken cancellationToken)
    {
        using var response = await session.SendCajaOnceAsync(actor,
            () => new HttpRequestMessage(HttpMethod.Get, $"api/caja/operaciones/{operation:D}"), true, cancellationToken);
        return await ParseAsync(response, cancellationToken);
    }

    public async Task<CajaRecoveryOutcome> CancelAsync(Guid operation, int actor, bool confirmaIntervencion, CancellationToken cancellationToken)
    {
        using var response = await session.SendCajaOnceAsync(actor, () => new HttpRequestMessage(HttpMethod.Post, $"api/caja/operaciones/{operation:D}/cancelacion")
        {
            Content = JsonContent.Create(new { confirmaIntervencion })
        }, false, cancellationToken);
        return await ParseAsync(response, cancellationToken);
    }

    public async Task<CajaRecoveryOutcome> ExecuteAsync(Guid operation, int actor, CancellationToken cancellationToken)
    {
        using var response = await session.SendCajaOnceAsync(actor, () => new HttpRequestMessage(HttpMethod.Post, $"api/caja/operaciones/{operation:D}/ejecucion")
        {
            Content = JsonContent.Create(new { })
        }, false, cancellationToken);
        return await ParseAsync(response, cancellationToken);
    }

    public async Task<bool> ReconcileAsync(Guid operation, int actor, string type, CancellationToken cancellationToken)
    {
        if (sink is null || operation == Guid.Empty || actor <= 0 || type is not ("AbrirTurno" or "CerrarTurno" or "CobrarCuenta")) return false;
        // A business GET alone cannot prove terminality. Fetch the same durable operation first.
        using var operationResponse = await session.SendCajaOnceAsync(actor,
            () => new HttpRequestMessage(HttpMethod.Get, $"api/caja/operaciones/{operation:D}"), true, cancellationToken);
        if (!operationResponse.IsSuccessStatusCode && (int)operationResponse.StatusCode is not (>= 400 and < 500)) return false;
        using var terminal = await ReadObjectAsync(operationResponse, cancellationToken);
        if (terminal is null || !TryString(terminal.RootElement, "estado", out var state)) return false;
        if (state is "Rechazada" or "Cancelada")
        {
            if ((state == "Rechazada" && ((int)operationResponse.StatusCode is < 400 or >= 500)) ||
                (state == "Cancelada" && !operationResponse.IsSuccessStatusCode)) return false;
            var unsuccessfulCode = state == "Rechazada" && TryString(terminal.RootElement, "codigo", out var rejectionCode) ? rejectionCode : null;
            if (state == "Rechazada" && string.IsNullOrWhiteSpace(unsuccessfulCode)) return false;
            return await ReconcileUnsuccessfulAsync(operation, actor, type, state, unsuccessfulCode, cancellationToken);
        }
        if (state != "Completada" || !operationResponse.IsSuccessStatusCode) return false;
        if (type == "CobrarCuenta") return await ReconcilePaymentAsync(operation, actor, terminal.RootElement, cancellationToken);
        if (
            !TryString(terminal.RootElement, "codigo", out var code) ||
            code != (type == "AbrirTurno" ? "TURNO_ABIERTO" : "TURNO_CERRADO") ||
            !TryInt(terminal.RootElement, "idTurnoCaja", out var id) || id <= 0) return false;

        return await ReconcileShiftAsync(id, actor, type == "CerrarTurno", cancellationToken);
    }

    private async Task<bool> ReconcileUnsuccessfulAsync(Guid operation, int actor, string type, string state, string? code, CancellationToken ct)
    {
        using var response = await session.SendCajaOnceAsync(actor,
            () => new HttpRequestMessage(HttpMethod.Get, $"api/caja/operaciones/{operation:D}/evidencia-conciliacion"), true, ct);
        if (!response.IsSuccessStatusCode) return false;
        using var evidence = await ReadObjectAsync(response, ct);
        if (evidence is null) return false;
        var root = evidence.RootElement;
        if (!TryString(root, "idOperacion", out var text) || !Guid.TryParse(text, out var id) || id != operation ||
            !TryString(root, "estado", out var actualState) || actualState != state ||
            !root.TryGetProperty("sinIntencion", out var noIntent) || noIntent.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        if (noIntent.ValueKind == JsonValueKind.True)
        {
            if (state != "Cancelada" || !IsNull(root, "tipo") || !IsNull(root, "idPedido") || !IsNull(root, "idTurnoCaja")) return false;
            return await ReconcileBarrierAsync(actor, type, ct);
        }
        if (!TryString(root, "tipo", out var actualType) || actualType != type) return false;
        if (type == "AbrirTurno")
        {
            if (!IsNull(root, "idPedido") || !IsNull(root, "idTurnoCaja")) return false;
            return await ReconcileCurrentShiftAsync(actor, ct);
        }
        if (type == "CobrarCuenta")
        {
            if (!TryInt(root, "idPedido", out var orderId) || orderId <= 0 ||
                !TryInt(root, "idTurnoCaja", out var paymentShiftId) || paymentShiftId <= 0) return false;
            var account = await ReadAccountAsync(orderId, actor, ct);
            if (account is null) return false;
            var shift = await ReadShiftAsync(paymentShiftId, actor, ct);
            if (state == "Rechazada" && code == "REFERENCIA_REQUIERE_CONFIRMACION")
                return shift is not null && sink is ICajaRejectionSnapshotSink rejectionSink &&
                    await rejectionSink.ApplyPaymentRejectionAsync(code, account, shift, ct);
            return shift is not null && sink is not null && await sink.ApplyAccountAndShiftAsync(account, shift, ct);
        }
        if (type != "CerrarTurno" || !IsNull(root, "idPedido") ||
            !TryInt(root, "idTurnoCaja", out var shiftId) || shiftId <= 0) return false;
        return await ReconcileShiftAsync(shiftId, actor, false, ct);
    }

    private async Task<bool> ReconcileCurrentShiftAsync(int actor, CancellationToken ct)
    {
        if (sink is null) return false;
        var (valid, snapshot) = await ReadCurrentShiftAsync(actor, ct);
        return valid && await sink.ApplyCurrentShiftAsync(snapshot, ct);
    }

    private async Task<bool> ReconcileBarrierAsync(int actor, string type, CancellationToken ct)
    {
        if (sink is null) return false;
        var (valid, snapshot) = await ReadCurrentShiftAsync(actor, ct);
        return valid && await sink.ApplyBarrierAsync(type, snapshot, ct);
    }

    public async Task<(bool Valid, CajaTurnoSnapshot? Snapshot)> ReadCurrentShiftAsync(int actor, CancellationToken ct)
    {
        using var response = await session.SendCajaOnceAsync(actor,
            () => new HttpRequestMessage(HttpMethod.Get, "api/caja/turno/actual"), true, ct);
        if (!response.IsSuccessStatusCode) return (false, null);
        using var document = await ReadObjectAsync(response, ct);
        if (document is null || !document.RootElement.TryGetProperty("turno", out var turno)) return (false, null);
        if (turno.ValueKind == JsonValueKind.Null) return (true, null);
        if (!TryShift(turno, out var snapshot) || snapshot!.Estado != "Abierto") return (false, null);
        return (true, snapshot);
    }

    private async Task<bool> ReconcileShiftAsync(int id, int actor, bool requireClosed, CancellationToken cancellationToken)
    {
        if (sink is null) return false;
        var snapshot = await ReadShiftAsync(id, actor, cancellationToken);
        if (snapshot is null || (requireClosed && snapshot.Estado != "Cerrado")) return false;
        return await sink.ApplyShiftAsync(snapshot, cancellationToken);
    }

    private async Task<CajaTurnoSnapshot?> ReadShiftAsync(int id, int actor, CancellationToken cancellationToken)
    {
        using var shiftResponse = await session.SendCajaOnceAsync(actor,
            () => new HttpRequestMessage(HttpMethod.Get, $"api/caja/turnos/{id}/resumen"), true, cancellationToken);
        if (!shiftResponse.IsSuccessStatusCode) return null;
        using var shift = await ReadObjectAsync(shiftResponse, cancellationToken);
        return shift is not null && TryShift(shift.RootElement, out var snapshot) && snapshot!.IdTurnoCaja == id ? snapshot : null;
    }

    private async Task<CajaCuentaRecoverySnapshot?> ReadAccountAsync(int id, int actor, CancellationToken ct)
    {
        string? cursor = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var previousId = 0;
        // Bound work on an invalid or unexpectedly large history; failure keeps the journal locked.
        for (var page = 0; page < 100; page++)
        {
            var path = "api/caja/cuentas?limite=100" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            using var response = await session.SendCajaOnceAsync(actor, () => new HttpRequestMessage(HttpMethod.Get, path), true, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var document = await ReadObjectAsync(response, ct, 1048576);
            if (document is null || !document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array ||
                items.GetArrayLength() > 100 || !document.RootElement.TryGetProperty("cursorSiguiente", out var next) ||
                next.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) return null;
            CajaCuentaRecoverySnapshot? found = null;
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    item.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != item.EnumerateObject().Count() ||
                    !TryInt(item, "idPedido", out var itemId) || itemId <= previousId) return null;
                previousId = itemId;
                if (itemId != id) continue;
                if (!TryString(item, "estado", out var state) || state is not ("Abierto" or "En Preparación" or "Listo" or "Pagado" or "Cancelado") ||
                    !TryMoney(item, "total", out var total) || !item.TryGetProperty("cobrable", out var eligible) ||
                    eligible.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                    !item.TryGetProperty("motivo", out var reason) || reason.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) return null;
                var motive = reason.ValueKind == JsonValueKind.Null ? null : reason.GetString();
                if (eligible.GetBoolean() ? motive is not null : string.IsNullOrWhiteSpace(motive)) return null;
                found = new CajaCuentaRecoverySnapshot(id, state, total, eligible.GetBoolean(), motive);
            }
            if (next.ValueKind == JsonValueKind.String)
            {
                cursor = next.GetString();
                if (string.IsNullOrEmpty(cursor) || cursor.Length > 256 || items.GetArrayLength() == 0 ||
                    cursor.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_') || !seen.Add(cursor)) return null;
            }
            if (found is not null) return found;
            if (next.ValueKind == JsonValueKind.Null || previousId >= id) return null;
        }
        return null;
    }

    private static bool TryShift(JsonElement root, out CajaTurnoSnapshot? snapshot)
    {
        snapshot = null;
        if (root.ValueKind != JsonValueKind.Object ||
            root.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count() ||
            !TryInt(root, "idTurnoCaja", out var id) || id <= 0 ||
            !TryInt(root, "idUsuarioApertura", out var opener) || opener <= 0 ||
            !TryString(root, "estado", out var state) || state is not ("Abierto" or "Cerrado") ||
            !TryMoney(root, "fondoInicial", out var fund) || !TryMoney(root, "totalEfectivo", out var cash) ||
            !TryMoney(root, "totalTarjeta", out var card) || !TryMoney(root, "totalTransferencia", out var transfer) ||
            !TryMoney(root, "efectivoEsperado", out var expected) ||
            !TryInt(root, "cantidadCuentasPendientes", out var pending) || pending < 0 || expected != fund + cash) return false;
        snapshot = new CajaTurnoSnapshot(id, state, fund, cash, card, transfer, expected, pending, opener);
        return true;
    }

    private static bool IsNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Null;

    private async Task<bool> ReconcilePaymentAsync(Guid operation, int actor, JsonElement terminal, CancellationToken ct)
    {
        if (sink is null || !TryLong(terminal, "idCobro", out var paymentId) || paymentId <= 0) return false;
        using var response = await session.SendCajaOnceAsync(actor,
            () => new HttpRequestMessage(HttpMethod.Get, $"api/caja/comprobantes/{paymentId}"), true, ct);
        if (!response.IsSuccessStatusCode) return false;
        using var receipt = await ReadObjectAsync(response, ct, 1048576);
        if (receipt is null) return false;
        return await ApplyPaymentReceiptAsync(operation, terminal, receipt.RootElement, ct);
    }

    private async Task<bool> ApplyPaymentReceiptAsync(Guid operation, JsonElement terminal, JsonElement root, CancellationToken ct, bool history = false)
    {
        if (sink is null || !TryString(terminal, "codigo", out var code) || code != "COBRO_REGISTRADO" ||
            !TryLong(terminal, "idCobro", out var paymentId) || paymentId <= 0 ||
            !TryLong(terminal, "numeroComprobante", out var number) || number <= 0 ||
            !TryInt(terminal, "idPedido", out var orderId) || orderId <= 0 ||
            !TryInt(terminal, "idTurnoCaja", out var shiftId) || shiftId <= 0 ||
            !TryMoney(terminal, "total", out var total) || total <= 0 ||
            !TryString(terminal, "metodoPago", out var method) || method is not ("Efectivo" or "Tarjeta" or "Transferencia")) return false;
        if (!TryString(root, "leyenda", out var legend) || legend != "COMPROBANTE INTERNO — DEMOSTRACIÓN — NO ES FACTURA FISCAL" ||
            !TryString(root, "fechaHora", out var dateText) || !DateTimeOffset.TryParse(dateText, out var date) || date.Offset != TimeSpan.Zero ||
            !TryDestination(root, out var destination) ||
            !TryLong(root, "idCobro", out var actualPayment) || actualPayment != paymentId ||
            !TryLong(root, "numeroComprobante", out var actualNumber) || actualNumber != number ||
            !TryInt(root, "idPedido", out var actualOrder) || actualOrder != orderId ||
            !TryInt(root, "idTurnoCaja", out var actualShift) || actualShift != shiftId ||
            !TryInt(root, "idUsuario", out var owner) || owner <= 0 ||
            !TryString(root, "idOperacion", out var operationText) || !Guid.TryParse(operationText, out var actualOperation) || actualOperation != operation ||
            !TryMoney(root, "total", out var actualTotal) || actualTotal != total ||
            !TryMoney(root, "subtotal", out var subtotal) || subtotal != total ||
            !TryMoney(root, "impuestos", out var taxes) || taxes != 0 ||
            !TryString(root, "metodoPago", out var actualMethod) || actualMethod != method ||
            !root.TryGetProperty("lineas", out var lines) || lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() == 0) return false;
        var snapshots = new List<CajaPagoLineaSnapshot>();
        decimal sum = 0;
        foreach (var line in lines.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.Object || line.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != line.EnumerateObject().Count() ||
                !TryInt(line, "orden", out var position) || position != snapshots.Count + 1 ||
                !TryString(line, "nombreProducto", out var name) || string.IsNullOrWhiteSpace(name) ||
                !TryInt(line, "cantidad", out var quantity) || quantity is < 1 or > 99 ||
                !TryMoney(line, "precioUnitario", out var price) || price <= 0 ||
                !TryMoney(line, "subtotal", out var lineTotal) || lineTotal != price * quantity) return false;
            sum += lineTotal;
            if (sum > 9999999999.99m) return false;
            snapshots.Add(new CajaPagoLineaSnapshot(position, name, quantity, price, lineTotal));
        }
        if (sum != total) return false;
        decimal? cash = null, change = null;
        string? externalReference = null;
        if (method == "Efectivo")
        {
            if (!TryMoney(root, "efectivoRecibido", out var received) || !TryMoney(root, "vuelto", out var returned) ||
                received < total || returned != received - total ||
                !TryMoney(terminal, "efectivoRecibido", out var terminalReceived) || terminalReceived != received ||
                !TryMoney(terminal, "vuelto", out var terminalChange) || terminalChange != returned || !IsNull(root, "referenciaExterna")) return false;
            cash = received;
            change = returned;
        }
        else
        {
            if (!IsNull(root, "efectivoRecibido") || !IsNull(root, "vuelto") ||
                !TryString(root, "referenciaExterna", out var reference) || !ValidExternalReference(reference)) return false;
            externalReference = reference;
        }
        var snapshot = new CajaPagoSnapshot(paymentId, number, orderId, shiftId, owner, total, method!, snapshots, cash, change, date, destination, externalReference);
        return history
            ? sink is ICajaHistorySnapshotSink historySink && await historySink.ApplyHistoryPaymentAsync(snapshot, ct)
            : await sink.ApplyPaymentAsync(snapshot, ct);
    }

    private static bool TryDestination(JsonElement root, out string? destination)
    {
        destination = null;
        if (!root.TryGetProperty("numeroMesa", out var table) || !root.TryGetProperty("idGrupo", out var group) ||
            !root.TryGetProperty("numerosMesasGrupo", out var members) || members.ValueKind != JsonValueKind.Array) return false;
        if (table.ValueKind == JsonValueKind.Number && table.TryGetInt32(out var number) && number > 0 &&
            group.ValueKind == JsonValueKind.Null && members.GetArrayLength() == 0)
        {
            destination = $"Mesa {number}";
            return true;
        }
        if (table.ValueKind != JsonValueKind.Null || group.ValueKind != JsonValueKind.Number || !group.TryGetInt32(out var groupId) || groupId <= 0 ||
            members.GetArrayLength() is < 2 or > 15) return false;
        var numbers = new List<int>();
        foreach (var member in members.EnumerateArray())
        {
            if (member.ValueKind != JsonValueKind.Number || !member.TryGetInt32(out var value) || value <= 0 ||
                (numbers.Count > 0 && value <= numbers[^1])) return false;
            numbers.Add(value);
        }
        destination = $"Grupo {groupId} · mesas {string.Join(", ", numbers)}";
        return true;
    }

    private static bool ValidExternalReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return false;
        try
        {
            return reference == reference.Normalize(NormalizationForm.FormC).Trim() &&
                reference.EnumerateRunes().Count() <= 100 && !reference.EnumerateRunes().Any(Rune.IsControl);
        }
        catch (ArgumentException) { return false; }
    }

    private static async Task<JsonDocument?> ReadObjectAsync(HttpResponseMessage response, CancellationToken ct, int limit = 16384)
    {
        if (response.Content.Headers.ContentLength > limit) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[limit + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), ct);
            if (read == 0) break;
            count += read;
        }
        if (count > limit) return null;
        var document = JsonDocument.Parse(buffer.AsMemory(0, count));
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() == document.RootElement.EnumerateObject().Count())
            return document;
        document.Dispose();
        return null;
    }

    private static bool TryString(JsonElement json, string name, out string? value)
    {
        value = null;
        if (!json.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString();
        return value is not null;
    }
    private static bool TryInt(JsonElement json, string name, out int value)
    {
        value = 0;
        return json.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }
    private static bool TryLong(JsonElement json, string name, out long value)
    {
        value = 0;
        return json.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }
    private static bool TryMoney(JsonElement json, string name, out decimal value)
    {
        value = 0;
        return json.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value) &&
            value >= 0 && value <= 9999999999.99m && decimal.Round(value, 2) == value;
    }

    private static async Task<CajaRecoveryOutcome> ParseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.NotFound) return CajaRecoveryOutcome.NotFound;
        if ((int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return CajaRecoveryOutcome.Uncertain;
        if (response.Content.Headers.ContentLength > 16384) return CajaRecoveryOutcome.Uncertain;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var bytes = new byte[16385];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count), ct);
            if (read == 0) break;
            count += read;
        }
        if (count > 16384) return CajaRecoveryOutcome.Uncertain;
        using var json = JsonDocument.Parse(bytes.AsMemory(0, count));
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return CajaRecoveryOutcome.Uncertain;
        string? state = null;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name != "estado") continue;
            if (state is not null || property.Value.ValueKind != JsonValueKind.String) return CajaRecoveryOutcome.Uncertain;
            state = property.Value.GetString();
        }
        return state switch
        {
            "Preparada" when response.IsSuccessStatusCode => CajaRecoveryOutcome.Prepared,
            "Completada" when response.IsSuccessStatusCode => CajaRecoveryOutcome.Completed,
            "Cancelada" when response.IsSuccessStatusCode => CajaRecoveryOutcome.Cancelled,
            "Rechazada" when (int)response.StatusCode is >= 400 and < 500 => CajaRecoveryOutcome.Rejected,
            _ => CajaRecoveryOutcome.Uncertain
        };
    }
}
