using System.Net;
using System.Text;
using Farm360.Core.Platform;
using Microsoft.AspNetCore.Http;

namespace Farm360.Identity.Api;

/// <summary>Tiny server-rendered pages for the sign-in flow. All values are HTML-encoded; no inline scripts or styles.</summary>
internal static class Html
{
    public static IResult Page(string title, string tenantName, string body, string? script = null, int status = 200) =>
        Results.Content(Layout(title, tenantName, body, script), "text/html; charset=utf-8", Encoding.UTF8, status);

    public static string E(string? value) => WebUtility.HtmlEncode(value ?? "");

    public static string Field(string name, string label, string type = "text", string? value = null, string? autocomplete = null, bool required = true) =>
        $"""
        <p><label for="{name}">{E(label)}</label><br>
        <input id="{name}" name="{name}" type="{type}" value="{E(value)}"{(autocomplete is null ? "" : $" autocomplete=\"{autocomplete}\"")}{(required ? " required" : "")}></p>
        """;

    public static string Hidden(string name, string? value) => $"""<input type="hidden" name="{name}" value="{E(value)}">""";

    public static string Errors(IEnumerable<string>? errors) =>
        errors is null || !errors.Any()
            ? ""
            : $"""<ul role="alert">{string.Concat(errors.Select(e => $"<li>{E(e)}</li>"))}</ul>""";

    private static string Layout(string title, string tenantName, string body, string? script) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta name="referrer" content="same-origin">
        <title>{{E(title)}} · {{E(tenantName)}}</title>
        <link rel="stylesheet" href="/account/style.css">
        </head>
        <body>
        <main>
        <h1>{{E(title)}}</h1>
        <p class="tenant">{{E(tenantName)}}</p>
        {{body}}
        </main>
        {{(script is null ? "" : $"<script src=\"{script}\" defer></script>")}}
        </body>
        </html>
        """;

    /// <summary>The sign-in pages' stylesheet. Every value comes from the design tokens (frontend/.../tokens.ts), never from here.</summary>
    public static readonly string Css = $$"""
        :root { color-scheme: light dark; --fg: {{DesignTokens.ColorTextLight}}; --bg: {{DesignTokens.ColorBackgroundPaperLight}}; --accent: {{DesignTokens.ColorPrimary}}; --on-accent: {{DesignTokens.ColorOnPrimaryLight}}; --muted: {{DesignTokens.ColorTextSecondaryLight}}; --border: {{DesignTokens.ColorBorderLight}}; --error: {{DesignTokens.ColorError}}; }
        @media (prefers-color-scheme: dark) { :root { --fg: {{DesignTokens.ColorTextDark}}; --bg: {{DesignTokens.ColorBackgroundDark}}; --accent: {{DesignTokens.ColorPrimaryOnDark}}; --on-accent: {{DesignTokens.ColorOnPrimaryDark}}; --muted: {{DesignTokens.ColorTextSecondaryDark}}; --border: {{DesignTokens.ColorBorderDark}}; --error: {{DesignTokens.ColorErrorOnDark}}; } }
        body { margin: 0; font: {{DesignTokens.FontSizeReading}}px/{{DesignTokens.FontLineHeightBody}} {{DesignTokens.FontFamily}}; color: var(--fg); background: var(--bg); }
        main { max-width: 26rem; margin: 3rem auto; padding: 0 1rem; }
        .tenant { color: var(--muted); margin-top: -0.5rem; }
        input { box-sizing: border-box; width: 100%; padding: .6rem; font: inherit; border: 1px solid var(--border); border-radius: {{DesignTokens.RadiusMd}}px; color: inherit; background: transparent; }
        button { padding: .6rem 1rem; font: inherit; font-weight: {{DesignTokens.FontWeightMedium}}; border: 0; border-radius: {{DesignTokens.RadiusMd}}px; color: var(--on-accent); background: var(--accent); cursor: pointer; }
        button.secondary { color: var(--accent); background: transparent; border: 1px solid var(--accent); }
        :focus-visible { outline: 3px solid var(--accent); outline-offset: 2px; }
        [role=alert] { color: var(--error); padding-left: 1.2rem; }
        a { color: var(--accent); }
        """;
}
