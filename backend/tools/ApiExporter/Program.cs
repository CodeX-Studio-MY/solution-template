using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Acme.Core.Composition;
using Acme.Core.Errors;
using Acme.Core.Hosting;
using Acme.Core.Messaging;
using Acme.Core.Platform;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Usage: ApiExporter <repo-root> [--check]   (--check writes nothing and fails if a file would change)
var root = args.FirstOrDefault() ?? throw new InvalidOperationException("Pass the repository root.");
var check = args.Contains("--check");
var outOfDate = new List<string>();
var modules = LoadModules().OrderBy(m => ServiceOf(m).Key, StringComparer.Ordinal).ToList();

var openApiDir = Path.Combine(root, "frontend", "packages", "api-clients", "openapi");
if (!check)
    Directory.CreateDirectory(openApiDir);
foreach (var module in modules)
    Write(Path.Combine(openApiDir, $"{ServiceOf(module).Key}.json"), await DocumentAsync(module) + "\n");

Write(Path.Combine(root, "frontend", "packages", "web-core", "src", "generated", "errors.ts"), ErrorCodes());
Write(Path.Combine(root, "frontend", "packages", "web-core", "src", "generated", "features.ts"), FeatureKeys());
Write(Path.Combine(root, "frontend", "packages", "web-core", "src", "generated", "custom-fields.ts"), WellKnownFields());
Write(Path.Combine(root, "docs", "events.md"), Events());
foreach (var path in outOfDate)
    Console.WriteLine($"out of date: {Path.GetRelativePath(root, path)}");
Console.WriteLine($"Exported {modules.Count} OpenAPI documents, the error codes and the event catalog{(check ? $" ({outOfDate.Count} out of date)" : "")}.");
return check && outOfDate.Count > 0 ? 1 : 0;

void Write(string path, string content)
{
    content = content.Replace("\r\n", "\n", StringComparison.Ordinal);
    if (File.Exists(path) && File.ReadAllText(path) == content)
        return;
    outOfDate.Add(path);
    if (!check)
        File.WriteAllText(path, content, new UTF8Encoding(false));
}

static IEnumerable<Type> LoadModules()
{
    // Module assemblies are referenced for the sake of this lookup only, so load them from the output folder.
    foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, $"{PlatformInfo.Product[..1].ToUpperInvariant()}{PlatformInfo.Product[1..]}.*.dll"))
    {
        try { Assembly.LoadFrom(path); }
        catch (BadImageFormatException) { }
    }
    return AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); } })
        .Where(t => t is { IsAbstract: false, IsInterface: false } && t.GetInterfaces().Any(i => i == typeof(IPlatformModule)));
}

static async Task<string> DocumentAsync(Type moduleType)
{
    var service = ServiceOf(moduleType);
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
    builder.WebHost.UseTestServer();
    foreach (var (key, value) in Settings())
        builder.Configuration[key] = value;

    typeof(PlatformHostBuilderExtensions).GetMethod(nameof(PlatformHostBuilderExtensions.AddPlatform))!.Invoke(null, [builder, new[] { service }]);
    typeof(PlatformHostBuilderExtensions).GetMethod(nameof(PlatformHostBuilderExtensions.AddModule))!.MakeGenericMethod(moduleType).Invoke(null, [builder]);
    // Nothing runs: no consumers, no relay, no migrations. Only the endpoints are described.
    builder.Services.RemoveAll<IHostedService>();
    builder.Services.AddOpenApi("v1", options => options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = $"{service.Key} API";
        return Task.CompletedTask;
    }));

    await using var app = builder.Build();
    app.UsePlatform();
    await app.StartAsync();   // endpoints are only collected into the route table when the host starts
    var provider = app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
    var document = await provider.GetOpenApiDocumentAsync();
    using var writer = new StringWriter();
    var json = new Microsoft.OpenApi.OpenApiJsonWriter(writer);
    document.SerializeAsV3(json);
    return JsonSerializer.Serialize(JsonDocument.Parse(writer.ToString()).RootElement, ExportJson.Indented);
}

static PlatformService ServiceOf(Type moduleType) =>
    (PlatformService)moduleType.GetProperty(nameof(IPlatformModule.Service), BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

static Dictionary<string, string> Settings()
{
    using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    return new()
    {
        ["Database:Provider"] = "postgresql",
        ["Database:CatalogConnectionString"] = "Host=export;Database=export;Username=x;Password=x",
        ["Database:Servers:default"] = "Host=export;Username=x;Password=x",
        ["Tenancy:BaseDomain"] = "localhost",
        ["Redis:ConnectionString"] = "localhost:1,abortConnect=false",
        ["DataProtection:KeyEncryptionKey"] = secret,
        ["RabbitMq:ConnectionString"] = "amqp://x:x@localhost:1",
        ["Storage:Provider"] = "local",
        ["Storage:LocalPath"] = Path.Combine(Path.GetTempPath(), "api-export"),
        ["Logging:LogLevel:Default"] = "Warning",
        ["Smtp:Host"] = "localhost",
        ["Smtp:FromAddress"] = "export@example.com",
        ["TokenServer:SigningKeys:0"] = signing.ExportECPrivateKeyPem(),
        ["TokenServer:EncryptionKeys:0"] = secret,
        ["Auth:MetadataAddress"] = "http://localhost:1/.well-known/openid-configuration",
        ["Auth:RequireHttpsMetadata"] = "false",
    };
}

static string ErrorCodes()
{
    var codes = AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); } })
        .Where(t => t.GetCustomAttribute<ErrorCatalogAttribute>() is not null)
        .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(ErrorCode)).Select(f => (ErrorCode)f.GetValue(null)!))
        .OrderBy(c => c.Code, StringComparer.Ordinal)
        .ToList();
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated> Edit the error catalogs in the backend and run scripts/generate.");
    sb.AppendLine("/** Every error code the API can return in ProblemDetails.code, with its HTTP status and message key. */");
    sb.AppendLine("export const errorCodes = {");
    foreach (var code in codes)
        sb.AppendLine(CultureInfo.InvariantCulture, $"  \"{code.Code}\": {{ status: {code.Status}, messageKey: \"{code.MessageKey}\" }},");
    sb.AppendLine("} as const;");
    sb.AppendLine();
    sb.AppendLine("export type ErrorCode = keyof typeof errorCodes;");
    return sb.ToString();
}

static string CamelCase(string key)
{
    var parts = key.Split(['.', '-'], StringSplitOptions.RemoveEmptyEntries);
    return parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
}

static string WellKnownFields()
{
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated> Edit WellKnownCustomFields in the backend's Contracts and run scripts/generate.");
    sb.AppendLine("/** Custom field keys that modules agree on, so one module's data can reach another without importing it. */");
    sb.AppendLine("export const wellKnownCustomFields = {");
    foreach (var field in typeof(Acme.Contracts.WellKnownCustomFields).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).OrderBy(f => f.Name, StringComparer.Ordinal))
        sb.AppendLine(CultureInfo.InvariantCulture, $"  {char.ToLowerInvariant(field.Name[0])}{field.Name[1..]}: \"{field.GetRawConstantValue()}\",");
    sb.AppendLine("} as const;");
    return sb.ToString();
}

static string FeatureKeys()
{
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated> Edit the feature flags in the backend's Contracts and run scripts/generate.");
    sb.AppendLine("/** Every feature flag, with the module it belongs to and its default. A flag a tenant hasn't set uses its default. */");
    sb.AppendLine("export const featureFlags = {");
    foreach (var feature in Acme.Core.Customization.CustomizationRegistry.Features.OrderBy(f => f.Key, StringComparer.Ordinal))
        sb.AppendLine(CultureInfo.InvariantCulture, $"  \"{feature.Key}\": {{ module: \"{feature.ModuleKey}\", defaultEnabled: {(feature.DefaultEnabled ? "true" : "false")} }},");
    sb.AppendLine("} as const;");
    sb.AppendLine();
    sb.AppendLine("export type FeatureKey = keyof typeof featureFlags;");
    sb.AppendLine();
    sb.AppendLine("/** The keys by a name that is safe to type: `features.purchaseRequestDraftSharing`. */");
    sb.AppendLine("export const features = {");
    foreach (var feature in Acme.Core.Customization.CustomizationRegistry.Features.OrderBy(f => f.Key, StringComparer.Ordinal))
        sb.AppendLine(CultureInfo.InvariantCulture, $"  {CamelCase(feature.Key)}: \"{feature.Key}\",");
    sb.AppendLine("} as const satisfies Record<string, FeatureKey>;");
    return sb.ToString();
}

static string Events()
{
    var sb = new StringBuilder();
    sb.AppendLine("<!-- <auto-generated> Edit the events in src/Contracts and run scripts/generate. -->");
    sb.AppendLine("# Integration events");
    sb.AppendLine();
    sb.AppendLine("Versioned events that cross module boundaries (`src/Contracts`). A breaking change gets a new version (`V2`) published next to the old one.");
    sb.AppendLine();
    sb.AppendLine("| Routing key | Payload | Fields |");
    sb.AppendLine("|---|---|---|");
    foreach (var descriptor in EventRegistry.All.OrderBy(d => d.RoutingKey, StringComparer.Ordinal))
    {
        var fields = descriptor.Type.GetProperties().Select(p => $"`{char.ToLowerInvariant(p.Name[0])}{p.Name[1..]}`: {TypeName(p.PropertyType)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| `{descriptor.RoutingKey}` | `{descriptor.Type.DeclaringType?.Name}.{descriptor.Type.Name}` | {string.Join(", ", fields)} |");
    }
    return sb.ToString();
}

static string TypeName(Type type)
{
    var underlying = Nullable.GetUnderlyingType(type);
    if (underlying is not null)
        return TypeName(underlying) + "?";
    return type.IsGenericType
        ? $"{type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>"
        : type.Name;
}

internal static class ExportJson
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
