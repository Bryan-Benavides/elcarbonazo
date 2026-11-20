namespace ElCarbonazo.Api.Entities;

public sealed class Ingrediente
{
    public int IdIngrediente { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string NombreClave { get; set; } = string.Empty;
    public string UnidadMedida { get; set; } = string.Empty;
    public decimal StockActual { get; set; }
    public decimal StockMinimo { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset FechaCreacionUtc { get; set; }
    public DateTimeOffset FechaActualizacionUtc { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class Receta
{
    public int IdReceta { get; set; }
    public int IdProducto { get; set; }
    public bool Activa { get; set; } = true;
    public string? NotasPreparacion { get; set; }
    public DateTimeOffset FechaActualizacionUtc { get; set; }
    public long Version { get; set; } = 1;
    public Producto Producto { get; set; } = null!;
    public ICollection<DetalleReceta> Detalles { get; set; } = new List<DetalleReceta>();
}

public sealed class DetalleReceta
{
    public int IdDetalleReceta { get; set; }
    public int IdReceta { get; set; }
    public int IdIngrediente { get; set; }
    public decimal CantidadRequerida { get; set; }
    public Receta Receta { get; set; } = null!;
    public Ingrediente Ingrediente { get; set; } = null!;
}

public sealed class MovimientoInventario
{
    public long IdMovimiento { get; set; }
    public int IdIngrediente { get; set; }
    public string TipoMovimiento { get; set; } = string.Empty;
    public decimal CantidadDelta { get; set; }
    public decimal StockAnterior { get; set; }
    public decimal StockPosterior { get; set; }
    public int? IdPedido { get; set; }
    public int? IdComanda { get; set; }
    public long? IdAnulacion { get; set; }
    public int IdUsuario { get; set; }
    public string? Motivo { get; set; }
    public DateTimeOffset FechaUtc { get; set; }
    public Ingrediente Ingrediente { get; set; } = null!;
    public Pedido? Pedido { get; set; }
    public Comanda? Comanda { get; set; }
    public Usuario Usuario { get; set; } = null!;
}
