using System.Net.Mime;
using Acme.Core.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Acme.Gateway.Http;

/// <summary>
/// Serves the built shell app and the frontend modules on every tenant host, so the app, the BFF and /api share one origin.
/// The shell's index.html is rendered per request: the Gateway injects the CSP style nonce that the shell passes to Emotion.
/// </summary>
internal static class ShellHost
{
    public const string NoncePlaceholder = "__CSP_NONCE__";

    public static void Use(WebApplication app)
    {
        var bff = app.Services.GetRequiredService<IOptions<BffOptions>>().Value;

        if (!string.IsNullOrEmpty(bff.ModulesRoot) && Directory.Exists(bff.ModulesRoot))
        {
            // Versioned module bundles are immutable; the manifest URL changes with the version.
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(bff.ModulesRoot),
                RequestPath = "/mfe",
                OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable",
            });
        }

        if (string.IsNullOrEmpty(bff.ShellRoot) || !Directory.Exists(bff.ShellRoot))
            return;

        var provider = new PhysicalFileProvider(bff.ShellRoot);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = provider,
            OnPrepareResponse = context =>
                context.Context.Response.Headers.CacheControl = context.File.Name == "index.html" ? "no-store" : "public, max-age=31536000, immutable",
        });

        // Single-page app: any other GET that isn't an API, BFF, health or module path gets index.html (with the nonce).
        app.MapFallback(async context =>
        {
            var path = context.Request.Path;
            if (!HttpMethods.IsGet(context.Request.Method)
                || path.StartsWithSegments("/api", StringComparison.Ordinal) || path.StartsWithSegments("/bff", StringComparison.Ordinal)
                || path.StartsWithSegments("/health", StringComparison.Ordinal) || path.StartsWithSegments("/mfe", StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            var index = provider.GetFileInfo("index.html");
            if (!index.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            using var reader = new StreamReader(index.CreateReadStream());
            var html = (await reader.ReadToEndAsync(context.RequestAborted)).Replace(NoncePlaceholder, (string)context.Items[SecurityHeadersMiddleware.NonceItem]!, StringComparison.Ordinal);
            context.Response.ContentType = MediaTypeNames.Text.Html;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync(html, context.RequestAborted);
        });
    }
}
