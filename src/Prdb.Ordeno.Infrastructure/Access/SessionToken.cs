using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Prdb.Ordeno.Infrastructure.Access;

/// <summary>
/// The opaque value that lives in the session cookie.
/// </summary>
internal static class SessionToken
{
    private const int ByteLength = 32;

    public static string Create() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ByteLength));

    /// <summary>
    /// Plain SHA-256, deliberately: this is a 256-bit random value, not a
    /// password, so there is nothing to slow an attacker down against — and the
    /// lookup happens on every request.
    /// </summary>
    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
