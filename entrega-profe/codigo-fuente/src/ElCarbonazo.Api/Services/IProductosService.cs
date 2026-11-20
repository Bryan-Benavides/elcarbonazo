using ElCarbonazo.Api.DTOs;

namespace ElCarbonazo.Api.Services;

public interface IProductosService
{
    Task<ProductoPageResult> GetPageAsync(
        bool disponibles,
        string? cursor,
        int limite,
        CancellationToken cancellationToken);
}

public sealed record ProductoPageResult(
    ProductoPageDto? Page = null,
    bool InvalidCursor = false);
