using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Acme.Generator;

public static class Naming
{
    /// <summary><c>purchase-request</c> → <c>PurchaseRequest</c>.</summary>
    public static string Pascal(string key) =>
        string.Concat(key.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary><c>purchase-request</c> → <c>purchaseRequest</c>.</summary>
    public static string Camel(string key)
    {
        var pascal = Pascal(key);
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    /// <summary><c>purchase-request</c> → <c>purchase_request</c> (database schema names).</summary>
    public static string Schema(string key) => key.Replace('-', '_');

    /// <summary>A name-based (RFC 9562 version 5) UUID, so seed data keeps the same IDs on every run.</summary>
    public static Guid StableGuid(string name)
    {
        // A fixed namespace for this template's seed data.
        var namespaceId = new Guid("6f1c2b6e-8d3a-4f7e-9a51-2c0d4e8b7a13");
        var namespaceBytes = namespaceId.ToByteArray(bigEndian: true);
        var nameBytes = Encoding.UTF8.GetBytes(name);
#pragma warning disable CA5350 // SHA-1 is what RFC 9562 specifies for version 5; it is not used for security here.
        var hash = SHA1.HashData([.. namespaceBytes, .. nameBytes]);
#pragma warning restore CA5350
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    public static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    public static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
