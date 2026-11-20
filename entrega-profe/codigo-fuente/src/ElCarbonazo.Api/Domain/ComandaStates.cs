namespace ElCarbonazo.Api.Domain;

public static class ComandaStates
{
    public const string Abierta = "Abierta";
    public const string EnPreparacion = "En Preparación";
    public const string Lista = "Lista";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Abierta,
        EnPreparacion,
        Lista
    };

    public static IReadOnlySet<string> Active { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Abierta,
        EnPreparacion
    };
}
