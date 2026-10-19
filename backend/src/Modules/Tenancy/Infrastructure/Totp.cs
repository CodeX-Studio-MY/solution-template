using System.Security.Cryptography;
using System.Text;

namespace Saaskit.Tenancy.Infrastructure;

/// <summary>RFC 6238 time-based one-time passwords (HMAC-SHA1, 30 s, 6 digits), compatible with authenticator apps.</summary>
internal static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string NewSecret() => Base32(RandomNumberGenerator.GetBytes(20));

    public static string Uri(string issuer, string account, string secret) =>
        $"otpauth://totp/{System.Uri.EscapeDataString(issuer)}:{System.Uri.EscapeDataString(account)}?secret={secret}&issuer={System.Uri.EscapeDataString(issuer)}";

    /// <summary>Accepts the current code and the neighbouring windows (clock drift).</summary>
    public static bool Verify(string secret, string code, DateTimeOffset now, int windows = 1)
    {
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
            return false;
        var counter = now.ToUnixTimeSeconds() / 30;
        for (var offset = -windows; offset <= windows; offset++)
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(secret, counter + offset)), Encoding.ASCII.GetBytes(code)))
                return true;
        }
        return false;
    }

    internal static string Compute(string secret, long counter)
    {
        var key = FromBase32(secret);
        var message = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(counter));
#pragma warning disable CA5350 // HMAC-SHA1 is what RFC 6238 and authenticator apps specify.
        var hash = HMACSHA1.HashData(key, message);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var value = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Base32(byte[] bytes)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[] FromBase32(string text)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in text.TrimEnd('='))
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(char.ToUpperInvariant(c), StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return [.. bytes];
    }
}
