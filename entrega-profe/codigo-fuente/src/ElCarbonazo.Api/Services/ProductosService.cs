using System.Text;
using System.Text.Json;
using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Services;

public sealed class ProductosService(AppDbContext context) : IProductosService
{
    private static readonly JsonSerializerOptions CursorJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProductoPageResult> GetPageAsync(
        bool disponibles,
        string? cursor,
        int limite,
        CancellationToken cancellationToken)
    {
        if (limite is < 1 or > 100)
        {
            return new ProductoPageResult(InvalidCursor: false);
        }

        var lastId = 0;
        if (cursor is not null)
        {
            CursorPayload? payload;
            try
            {
                var json = Encoding.UTF8.GetString(DecodeBase64Url(cursor));
                payload = JsonSerializer.Deserialize<CursorPayload>(json, CursorJsonOptions);
            }
            catch (Exception exception) when (exception is FormatException or JsonException or DecoderFallbackException)
            {
                return new ProductoPageResult(InvalidCursor: true);
            }

            if (payload is null || payload.LastId <= 0 || payload.Disponibles != disponibles)
            {
                return new ProductoPageResult(InvalidCursor: true);
            }

            lastId = payload.LastId;
        }

        var query = context.Productos.AsNoTracking().AsQueryable();
        if (disponibles)
        {
            query = query.Where(product => product.Disponible);
        }

        if (lastId > 0)
        {
            query = query.Where(product => product.IdProducto > lastId);
        }

        var rows = await query
            .OrderBy(product => product.IdProducto)
            .Take(limite + 1)
            .Select(product => new ProductoDto(
                product.IdProducto,
                product.Nombre,
                product.Categoria,
                product.Precio,
                product.Disponible,
                product.Version))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > limite;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var nextCursor = hasMore
            ? EncodeBase64Url(JsonSerializer.SerializeToUtf8Bytes(
                new CursorPayload(rows[^1].IdProducto, disponibles),
                CursorJsonOptions))
            : null;
        return new ProductoPageResult(new ProductoPageDto(rows, nextCursor));
    }

    private static byte[] DecodeBase64Url(string value)
    {
        if (value.Length is 0 or > 256 || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new FormatException("Cursor inválido.");
        }

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
        return Convert.FromBase64String(base64);
    }

    private static string EncodeBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record CursorPayload(int LastId, bool Disponibles);
}
