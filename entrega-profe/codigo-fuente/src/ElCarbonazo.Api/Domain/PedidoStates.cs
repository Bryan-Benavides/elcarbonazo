namespace ElCarbonazo.Api.Domain;

public static class PedidoStates
{
    public const string Abierto = "Abierto";
    public const string EnPreparacion = "En Preparación";
    public const string Listo = "Listo";
    public const string Pagado = "Pagado";
    public const string Cancelado = "Cancelado";

    public static IReadOnlySet<string> Active { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Abierto,
        EnPreparacion,
        Listo
    };
}
