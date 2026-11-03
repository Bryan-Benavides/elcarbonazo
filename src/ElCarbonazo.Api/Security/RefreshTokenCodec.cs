using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace ElCarbonazo.Api.Security;

public static class RefreshTokenCodec
{
    public static string Create() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed class SessionExpiredException : Exception
{
    public SessionExpiredException() : base("The backing session is no longer valid.") { }
}
