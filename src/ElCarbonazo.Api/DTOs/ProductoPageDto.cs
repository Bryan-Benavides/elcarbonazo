namespace ElCarbonazo.Api.DTOs;

public sealed record ProductoPageDto(
    IReadOnlyList<ProductoDto> Items,
    string? CursorSiguiente);
