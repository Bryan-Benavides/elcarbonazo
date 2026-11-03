using System.Text;

namespace ElCarbonazo.Api.Domain;

public static class BootstrapPasswordPolicy
{
    public const int MinimumCharacters = 10;
    public const int MaximumUtf8Bytes = 64;

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "password1",
        "1234567890",
        "qwerty1234",
        "admin12345",
        "elcarbonazo",
        "managua123",
        "bienvenido",
        "restaurant1",
        "changeme123"
    };

    public static bool IsAcceptable(string password) =>
        password.EnumerateRunes().Count() >= MinimumCharacters &&
        Encoding.UTF8.GetByteCount(password) <= MaximumUtf8Bytes &&
        !CommonPasswords.Contains(password);
}
