namespace Acme.Core.Files;

/// <summary>Finds a file's real type from its first bytes (magic numbers), because the client's <c>Content-Type</c> and file name can lie.</summary>
public static class ContentSniffer
{
    private static readonly (byte[] Signature, string ContentType)[] Signatures =
    [
        ("%PDF-"u8.ToArray(), "application/pdf"),
        ([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "image/png"),
        ([0xFF, 0xD8, 0xFF], "image/jpeg"),
        ("GIF87a"u8.ToArray(), "image/gif"),
        ("GIF89a"u8.ToArray(), "image/gif"),
        ([0x50, 0x4B, 0x03, 0x04], "application/zip"),   // also .docx and .xlsx, which are zip containers
        ([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1], "application/x-ole-storage"),   // legacy .doc / .xls
    ];

    /// <summary>Common content types that share a sniffed container: a .docx is "application/zip" by magic number.</summary>
    private static readonly Dictionary<string, string[]> Containers = new(StringComparer.Ordinal)
    {
        ["application/zip"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
        ["application/x-ole-storage"] = ["application/msword", "application/vnd.ms-excel"],
    };

    /// <summary>The detected type, or <c>text/plain</c> for text without binary bytes, or null when it is not recognised.</summary>
    public static string? Detect(ReadOnlySpan<byte> header)
    {
        foreach (var (signature, contentType) in Signatures)
        {
            if (header.StartsWith(signature))
                return contentType;
        }
        return IsText(header) ? "text/plain" : null;
    }

    /// <summary>Whether a file whose bytes look like <paramref name="detected"/> may be stored as one of <paramref name="allowed"/>.</summary>
    public static bool Matches(string detected, IReadOnlyCollection<string> allowed) =>
        allowed.Contains(detected) || (Containers.TryGetValue(detected, out var aliases) && aliases.Any(allowed.Contains)) || (detected == "text/plain" && allowed.Contains("text/csv"));

    private static bool IsText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
            return false;
        foreach (var b in bytes)
        {
            if (b == 0 || (b < 0x09) || (b is > 0x0D and < 0x20))
                return false;
        }
        try
        {
            _ = new System.Text.UTF8Encoding(false, true).GetCharCount(bytes);
            return true;
        }
        catch (System.Text.DecoderFallbackException)
        {
            return false;
        }
    }
}
