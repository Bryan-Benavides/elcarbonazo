using System.Net;
using System.Net.Http.Json;
using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Services;

public interface IEnvioPedidoApiService
{
    Task<EnvioConfirmadoPedidoDto> EnviarAsync(IntencionPedido intencion, CancellationToken cancellationToken = default);
}

public sealed class EnvioPedidoApiService(ISessionManager session) : IEnvioPedidoApiService
{
    public async Task<EnvioConfirmadoPedidoDto> EnviarAsync(IntencionPedido intencion, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            intencion.EnvioId,
            intencion.MesaId,
            intencion.GrupoId,
            intencion.VersionMesa,
            Lineas = intencion.Lineas.Select(linea => new
            {
                linea.IdProducto,
                linea.VersionProducto,
                linea.Cantidad,
                linea.Notas
            }).ToArray()
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await session.SendHumanAsync(() => new HttpRequestMessage(HttpMethod.Post, "api/pedidos/envios")
        {
            Content = JsonContent.Create(body)
        }, false, timeout.Token);
        await MesaApiResponse.EnsureSuccessAsync(response, timeout.Token);
        if (response.StatusCode != HttpStatusCode.Created || response.Headers.Location is null)
            throw new InvalidDataException("La confirmación del envío no es válida.");
        var confirmed = await response.Content.ReadFromJsonAsync<EnvioConfirmadoPedidoDto>(cancellationToken: timeout.Token)
            ?? throw new InvalidDataException("La confirmación del envío está vacía.");
        timeout.Token.ThrowIfCancellationRequested();
        if (confirmed.Pedido is null || !CuentaPedidoApiService.EsValida(confirmed.Pedido, intencion.MesaId, intencion.GrupoId) ||
            confirmed.IdComanda <= 0 || confirmed.Pedido.SoloLectura || confirmed.Pedido.Estado != "Abierto" ||
            (intencion.MesaId is not null && confirmed.VersionMesa is null))
            throw new InvalidDataException("La confirmación del envío no corresponde al destino.");
        var matching = confirmed.Pedido.Comandas.Where(item => item.IdComanda == confirmed.IdComanda).ToArray();
        if (matching.Length != 1 || matching[0].Estado != "Abierta" || matching[0].Lineas.Count != intencion.Lineas.Count)
            throw new InvalidDataException("La confirmación no contiene la comanda esperada.");
        for (var i = 0; i < intencion.Lineas.Count; i++)
        {
            var expected = intencion.Lineas[i];
            var actual = matching[0].Lineas[i];
            if (actual.IdProducto != expected.IdProducto || actual.Cantidad != expected.Cantidad ||
                actual.NombreProducto != expected.Nombre || actual.PrecioUnitario != expected.PrecioUnitario || actual.Notas != expected.Notas)
                throw new InvalidDataException("La confirmación contiene líneas incompatibles con el borrador.");
        }
        return confirmed;
    }
}
