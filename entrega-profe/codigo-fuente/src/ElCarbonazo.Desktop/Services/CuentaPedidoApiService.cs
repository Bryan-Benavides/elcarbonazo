using ElCarbonazo.Desktop.Models;
using System.Text.Json;

namespace ElCarbonazo.Desktop.Services;

public interface ICuentaPedidoApiService
{
    Task<CuentaActivaPedidoDto> ConsultarAsync(int? mesaId, int? grupoId, CancellationToken cancellationToken = default);
}

public sealed class CuentaPedidoApiService(ISessionManager session) : ICuentaPedidoApiService
{
    public async Task<CuentaActivaPedidoDto> ConsultarAsync(int? mesaId, int? grupoId, CancellationToken cancellationToken = default)
    {
        if ((mesaId is null) == (grupoId is null) || mesaId is <= 0 || grupoId is <= 0)
            throw new ArgumentException("Seleccione exactamente una mesa o un grupo válido.");
        var path = mesaId is int mesa ? $"api/pedidos/activo?mesaId={mesa}" : $"api/pedidos/activo?grupoId={grupoId}";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var response = await session.SendPollingAsync(() => new HttpRequestMessage(HttpMethod.Get, path), timeout.Token);
        await MesaApiResponse.EnsureSuccessAsync(response, timeout.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("pedido", out _))
            throw new InvalidDataException("La API no indicó el estado de la cuenta.");
        var result = document.RootElement.Deserialize<CuentaActivaPedidoDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("La API devolvió una cuenta inválida.");
        timeout.Token.ThrowIfCancellationRequested();
        if (result.Pedido is { } order && !EsValida(order, mesaId, grupoId))
            throw new InvalidDataException("La API devolvió una cuenta inválida o de otro destino.");
        return result;
    }

    internal static bool EsValida(CuentaPedidoDto order, int? mesaId, int? grupoId) =>
        !(order.IdPedido <= 0 || order.IdMesa != mesaId || order.IdGrupo != grupoId ||
            order.IdUsuario <= 0 || order.Estado is not ("Abierto" or "En Preparación" or "Listo") ||
            order.Subtotal < 0 || order.Impuestos < 0 || order.Total != order.Subtotal + order.Impuestos ||
            order.Comandas is null || (order.Comandas.Count == 0) != order.SoloLectura ||
            (order.SoloLectura && string.IsNullOrWhiteSpace(order.Motivo)) ||
            order.Comandas.Any(command => command is null || command.IdComanda <= 0 ||
                command.IdUsuario <= 0 || command.Estado is not ("Abierta" or "En Preparación" or "Lista") ||
                command.Lineas is null || command.Lineas.Count is < 1 or > 50 || command.Lineas.Any(line => line is null || line.IdDetalle <= 0 ||
                    line.IdProducto <= 0 || string.IsNullOrWhiteSpace(line.NombreProducto) || line.Cantidad is < 1 or > 99 ||
                    line.PrecioUnitario <= 0 || line.Subtotal != line.Cantidad * line.PrecioUnitario || line.Notas is null)) ||
            order.Comandas.Select(command => command.IdComanda).Distinct().Count() != order.Comandas.Count ||
            order.Comandas.SelectMany(command => command.Lineas).Select(line => line.IdDetalle).Distinct().Count() !=
                order.Comandas.Sum(command => command.Lineas.Count));
}
