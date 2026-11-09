using System.Text;

namespace ElCarbonazo.Desktop.Models;

/// <summary>Borrador local; la API vuelve a validar productos, versiones e importes al enviar.</summary>
public sealed class BorradorPedido
{
    public const int MaxLineas = 50;
    private readonly List<LineaBorradorPedido> _lineas = [];

    public IReadOnlyList<LineaBorradorPedido> Lineas => _lineas.AsReadOnly();
    public decimal Subtotal => _lineas.Sum(linea => linea.Cantidad * linea.PrecioUnitario);

    public void Agregar(int idProducto, string nombre, string categoria, decimal precio, int version)
    {
        if (_lineas.Count >= MaxLineas || idProducto <= 0 || version <= 0 ||
            string.IsNullOrWhiteSpace(nombre) || categoria is not ("Comida" or "Bebida" or "Licor") ||
            precio is < 0.01m or > 999999.99m || decimal.Round(precio, 2) != precio)
            throw new ArgumentException("El producto no es válido o el borrador alcanzó 50 líneas.");

        _lineas.Add(new LineaBorradorPedido(idProducto, nombre, categoria, precio, version, 1, string.Empty));
    }

    public void Actualizar(int indice, int cantidad, string? notas)
    {
        if (cantidad is < 1 or > 99 || !NotaPedido.TryNormalizar(notas, out var normalizada))
            throw new ArgumentException("Cantidad o nota inválida.");
        _lineas[indice] = _lineas[indice] with { Cantidad = cantidad, Notas = normalizada };
    }

    public void Retirar(int indice) => _lineas.RemoveAt(indice);
}

public sealed record LineaBorradorPedido(
    int IdProducto, string Nombre, string Categoria, decimal PrecioUnitario,
    int VersionProducto, int Cantidad, string Notas);

public static class NotaPedido
{
    public static bool TryNormalizar(string? nota, out string normalizada)
    {
        normalizada = string.Empty;
        try
        {
            var texto = (nota ?? string.Empty).Normalize(NormalizationForm.FormC);
            if (texto.EnumerateRunes().Count() > 150 || texto.EnumerateRunes().Any(Rune.IsControl))
                return false;
            normalizada = texto;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
