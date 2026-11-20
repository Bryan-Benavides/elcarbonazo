using System.Net.Mail;
using System.Text;

namespace ElCarbonazo.Api.Domain;

public static class UserIdentity
{
    public const int MinimumNameLength = 3;
    public const int MaximumNameLength = 50;
    public const int MaximumEmailLength = 254;

    public static string NormalizeName(string value) => Normalize(value);

    public static string NormalizeEmail(string value) => Normalize(value);

    public static bool IsValidName(string value)
    {
        var runes = value.EnumerateRunes().ToArray();
        if (runes.Length is < MinimumNameLength or > MaximumNameLength ||
            !Rune.IsLetterOrDigit(runes[0]) ||
            !Rune.IsLetterOrDigit(runes[^1]))
        {
            return false;
        }

        var previousWasSeparator = false;
        foreach (var rune in runes)
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                previousWasSeparator = false;
                continue;
            }

            if (previousWasSeparator || !IsAllowedSeparator(rune))
            {
                return false;
            }

            previousWasSeparator = true;
        }

        return true;
    }

    public static bool IsValidEmail(string value)
    {
        if (value.EnumerateRunes().Count() > MaximumEmailLength ||
            value.Any(char.IsWhiteSpace) ||
            !MailAddress.TryCreate(value, out var address))
        {
            return false;
        }

        return string.Equals(address.Address, value, StringComparison.Ordinal);
    }

    public static void NormalizeAndValidate(Entities.Usuario usuario)
    {
        usuario.Nombre = NormalizeName(usuario.Nombre);
        usuario.Email = NormalizeEmail(usuario.Email);

        if (!IsValidName(usuario.Nombre))
        {
            throw new InvalidOperationException(
                "El nombre de usuario no cumple el formato de identidad requerido.");
        }

        if (!IsValidEmail(usuario.Email))
        {
            throw new InvalidOperationException("El email no tiene un formato válido.");
        }
    }

    private static string Normalize(string value) =>
        value.Normalize(NormalizationForm.FormC).Trim().ToLowerInvariant();

    private static bool IsAllowedSeparator(Rune rune) =>
        rune.Value is ' ' or '.' or '-' or '_';
}
