using ElCarbonazo.Desktop.Models;

namespace ElCarbonazo.Desktop.Services;

public interface IProductosPedidoApiService
{
    Task<PaginaProductosDto> ConsultarAsync(string? cursor = null, CancellationToken cancellationToken = default);
}

public sealed class ProductosPedidoApiService(ISessionManager session) : IProductosPedidoApiService
{
    public async Task<PaginaProductosDto> ConsultarAsync(string? cursor = null, CancellationToken cancellationToken = default)
    {
        if (cursor is { Length: 0 or > 256 }) throw new ArgumentException("Cursor inválido.", nameof(cursor));
        var path = "api/productos?disponibles=true&limite=25";
        if (cursor is not null) path += $"&cursor={Uri.EscapeDataString(cursor)}";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var response = await session.SendPollingAsync(() => new HttpRequestMessage(HttpMethod.Get, path), timeout.Token);
        var page = await MesaApiResponse.ReadAsync<PaginaProductosDto>(response, timeout.Token);
        timeout.Token.ThrowIfCancellationRequested();
        if (page.Items is null || page.Items.Count > 25 || page.CursorSiguiente is { Length: 0 or > 256 } ||
            page.Items.Any(item => item is null || item.IdProducto <= 0 || item.Version <= 0 ||
                string.IsNullOrWhiteSpace(item.Nombre) || item.Categoria is not ("Comida" or "Bebida" or "Licor") ||
                !item.Disponible || item.Precio is < 0.01m or > 999999.99m || decimal.Round(item.Precio, 2) != item.Precio) ||
            page.Items.Select(item => item.IdProducto).Distinct().Count() != page.Items.Count)
            throw new InvalidDataException("La API devolvió un catálogo inválido.");
        return page;
    }
}
