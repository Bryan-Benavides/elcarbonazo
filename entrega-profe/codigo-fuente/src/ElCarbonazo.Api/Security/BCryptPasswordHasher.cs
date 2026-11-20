using System.Globalization;
using System.Security.Cryptography;
using ElCarbonazo.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ElCarbonazo.Api.Security;

public sealed class BCryptPasswordHasher(IOptions<PasswordHashingOptions> options) : IPasswordHasher
{
    private readonly int _workFactor = options.Value.WorkFactor;
    private readonly string _dummyHash = BCrypt.Net.BCrypt.HashPassword(
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        options.Value.WorkFactor);

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, _workFactor);

    public bool Verify(string password, string passwordHash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    public bool NeedsRehash(string passwordHash)
    {
        if (passwordHash.Length < 7 ||
            passwordHash[0] != '$' ||
            !int.TryParse(passwordHash.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var cost))
        {
            return false;
        }

        return cost < _workFactor;
    }

    public void VerifyDummy(string password)
    {
        _ = BCrypt.Net.BCrypt.Verify(password, _dummyHash);
    }
}
