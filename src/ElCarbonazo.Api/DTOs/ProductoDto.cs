namespace ElCarbonazo.Api.DTOs;

public sealed record ProductoDto(
    int IdProducto,
    string Nombre,
    string Categoria,
    decimal Precio,
    bool Disponible,
    int Version);
