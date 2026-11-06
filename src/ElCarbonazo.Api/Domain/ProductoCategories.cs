namespace ElCarbonazo.Api.Domain;

public static class ProductoCategories
{
    public const string Comida = "Comida";
    public const string Bebida = "Bebida";
    public const string Licor = "Licor";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Comida,
        Bebida,
        Licor
    };
}
