using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Farm360.Core.Platform;
using Farm360.Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Farm360.Core.Localization;

/// <summary>Turns a message key into text in the request's language. Unknown keys come back unchanged, so a missing translation is visible, not fatal.</summary>
public interface IMessageLocalizer
{
    /// <summary>The language used: the request's <c>Accept-Language</c> if supported, else the tenant's locale, else the default.</summary>
    string Locale { get; }

    string Text(string key, params object?[] arguments);
}

/// <summary>
/// The message catalog: JSON files <c>Resources/Messages.{locale}.json</c> (flat key → text, with {0} placeholders) embedded in
/// Core and in each module. One file per module per locale; no UI or API text is written in code.
/// </summary>
public sealed class MessageCatalog
{
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _byLocale = new(StringComparer.OrdinalIgnoreCase);

    public MessageCatalog(IEnumerable<IMessageCatalogContribution> contributions)
    {
        AddAssembly(typeof(MessageCatalog).Assembly);
        foreach (var contribution in contributions)
            AddAssembly(contribution.Assembly);
    }

    public IReadOnlyCollection<string> Locales => [.. _byLocale.Keys];

    /// <summary>Loads every embedded <c>*.Messages.{locale}.json</c> of an assembly.</summary>
    public void AddAssembly(Assembly assembly)
    {
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && n.Contains(".Messages.", StringComparison.OrdinalIgnoreCase)))
        {
            var locale = resource[(resource.LastIndexOf(".Messages.", StringComparison.OrdinalIgnoreCase) + ".Messages.".Length)..^".json".Length];
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var messages = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            var target = _byLocale.GetOrAdd(locale, _ => new Dictionary<string, string>(StringComparer.Ordinal));
            lock (target)
            {
                foreach (var (key, value) in messages)
                    target[key] = value;
            }
        }
    }

    public string? Find(string locale, string key) =>
        _byLocale.TryGetValue(locale, out var messages) && messages.TryGetValue(key, out var text) ? text : null;
}

internal sealed class MessageLocalizer(MessageCatalog catalog, IHttpContextAccessor http, ITenantContext tenant) : IMessageLocalizer
{
    private string? _locale;

    public string Locale => _locale ??= Resolve();

    public string Text(string key, params object?[] arguments)
    {
        var text = catalog.Find(Locale, key) ?? catalog.Find(PlatformInfo.DefaultLocales[0], key) ?? key;
        return arguments.Length == 0 ? text : string.Format(CultureInfo.GetCultureInfo(Locale), text, arguments);
    }

    private string Resolve()
    {
        var supported = PlatformInfo.DefaultLocales;
        var header = http.HttpContext?.Request.Headers.AcceptLanguage.ToString();
        if (!string.IsNullOrWhiteSpace(header))
        {
            // The first listed language we support, by its primary subtag ("zh-CN" → "zh").
            foreach (var part in header.Split(','))
            {
                var tag = part.Split(';')[0].Trim();
                var primary = tag.Split('-')[0].ToLowerInvariant();
                if (supported.Contains(primary))
                    return primary;
            }
        }
        if (tenant.HasTenant && supported.Contains(tenant.Tenant.Locale.Split('-')[0].ToLowerInvariant()))
            return tenant.Tenant.Locale.Split('-')[0].ToLowerInvariant();
        return supported[0];
    }
}

public static class LocalizationRegistration
{
    /// <summary>Registers the localizer and Core's own messages. Called by the kernel.</summary>
    public static IServiceCollection AddPlatformLocalization(this IServiceCollection services)
    {
        services.AddSingleton<MessageCatalog>();
        services.AddScoped<IMessageLocalizer, MessageLocalizer>();
        return services;
    }

    /// <summary>Adds a module's embedded message files (<c>Resources/Messages.{locale}.json</c>) to the catalog.</summary>
    public static IServiceCollection AddModuleMessages(this IServiceCollection services, Assembly moduleAssembly)
    {
        services.AddSingleton<IMessageCatalogContribution>(new AssemblyMessages(moduleAssembly));
        return services;
    }
}

/// <summary>An assembly whose message files join the catalog at start-up.</summary>
public interface IMessageCatalogContribution
{
    Assembly Assembly { get; }
}

internal sealed record AssemblyMessages(Assembly Assembly) : IMessageCatalogContribution;
