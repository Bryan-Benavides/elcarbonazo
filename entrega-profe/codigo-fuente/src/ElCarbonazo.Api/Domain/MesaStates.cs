namespace ElCarbonazo.Api.Domain;

public static class MesaStates
{
    public const string Libre = "Libre";
    public const string Ocupada = "Ocupada";
    public const string Reservada = "Reservada";
    public const string Agrupada = "Agrupada";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Libre,
        Ocupada,
        Reservada,
        Agrupada
    };
}
