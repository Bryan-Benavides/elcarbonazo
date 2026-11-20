namespace ElCarbonazo.Api.Domain;

public static class UserRoles
{
    public const string Jefa = "Jefa";
    public const string EncargadoCaja = "Encargado de Caja";
    public const string Mesero = "Mesero";
    public const string PersonalCocina = "Personal de Cocina";
    public const string Supervisor = "Supervisor";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Jefa,
        EncargadoCaja,
        Mesero,
        PersonalCocina,
        Supervisor
    };
}
