using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Acme.Identity.Infrastructure.OpenIddict;

/// <summary>JSON column helpers for the OpenIddict stores.</summary>
internal static class OidcJson
{
    public static ImmutableArray<string> Strings(string? json) =>
        string.IsNullOrEmpty(json) ? [] : [.. JsonSerializer.Deserialize<string[]>(json) ?? []];

    public static string? Strings(ImmutableArray<string> values) =>
        values.IsDefaultOrEmpty ? null : JsonSerializer.Serialize(values.ToArray());

    public static ImmutableDictionary<string, JsonElement> Properties(string? json) =>
        string.IsNullOrEmpty(json)
            ? ImmutableDictionary<string, JsonElement>.Empty
            : (JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? []).ToImmutableDictionary();

    public static string? Properties(ImmutableDictionary<string, JsonElement> values) =>
        values.IsEmpty ? null : JsonSerializer.Serialize(values);

    public static ImmutableDictionary<string, string> Settings(string? json) =>
        string.IsNullOrEmpty(json)
            ? ImmutableDictionary<string, string>.Empty
            : (JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? []).ToImmutableDictionary();

    public static string? Settings(ImmutableDictionary<string, string> values) =>
        values.IsEmpty ? null : JsonSerializer.Serialize(values);

    public static ImmutableDictionary<CultureInfo, string> DisplayNames(string? json) =>
        string.IsNullOrEmpty(json)
            ? ImmutableDictionary<CultureInfo, string>.Empty
            : (JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [])
                .ToImmutableDictionary(p => CultureInfo.GetCultureInfo(p.Key), p => p.Value);

    public static string? DisplayNames(ImmutableDictionary<CultureInfo, string> values) =>
        values.IsEmpty ? null : JsonSerializer.Serialize(values.ToDictionary(p => p.Key.Name, p => p.Value));

    public static JsonWebKeySet? KeySet(string? json) => string.IsNullOrEmpty(json) ? null : new JsonWebKeySet(json);

    /// <summary>Serializes public key parameters only. A key set never carries private material.</summary>
    public static string? KeySet(JsonWebKeySet? set)
    {
        if (set is null || set.Keys.Count == 0)
            return null;
        var keys = set.Keys.Select(k => new Dictionary<string, object?>
        {
            ["kty"] = k.Kty,
            ["kid"] = k.Kid,
            ["use"] = k.Use,
            ["alg"] = k.Alg,
            ["crv"] = k.Crv,
            ["x"] = k.X,
            ["y"] = k.Y,
            ["n"] = k.N,
            ["e"] = k.E,
        }.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value));
        return JsonSerializer.Serialize(new { keys });
    }

    public static DateTimeOffset? Offset(DateTime? value) => value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

    public static DateTime? Utc(DateTimeOffset? value) => value?.UtcDateTime;

    public static Guid ParseId(string identifier) =>
        Guid.TryParse(identifier, out var id) ? id : Guid.Empty;
}
