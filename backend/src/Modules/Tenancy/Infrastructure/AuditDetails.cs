using System.Text.Json;

namespace Saaskit.Tenancy.Infrastructure;

/// <summary>Reads one text field from an audit entry's JSON details, or null if the details are missing, not an object, or lack it.</summary>
internal static class AuditDetails
{
    public static string? Text(string? details, string name)
    {
        if (string.IsNullOrEmpty(details))
            return null;
        try
        {
            using var document = JsonDocument.Parse(details);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
