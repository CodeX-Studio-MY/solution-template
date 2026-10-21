using System.Text.Json;
using Saaskit.Core.Configuration;
using Saaskit.Core.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Saaskit.Gateway.Http;

/// <summary>
/// The operator console's backend-for-frontend, on <c>operator.{BaseDomain}</c> only. Sign-in goes through the Gateway, which keeps
/// the operator session token in an encrypted, HttpOnly cookie and adds it (<c>Authorization: Operator …</c>) to proxied operator
/// API calls. As for tenants, the browser never holds a token (ADR 0019).
/// </summary>
internal static class OperatorBff
{
    public const string CookieName = "__Host-operator";
    public const string HttpClientName = "operator";
    public const string RouteMetadata = "Operator";
    public const string TokenItem = "bff.operator-token";
    private const string Purpose = "Saaskit.Gateway.OperatorSession";

    public static bool IsOperatorHost(HttpContext context) =>
        context.Request.Host.Host.Equals(
            $"operator.{context.RequestServices.GetRequiredService<IOptions<TenancyOptions>>().Value.BaseDomain}",
            StringComparison.OrdinalIgnoreCase);

    public static IServiceCollection AddOperatorBff(this IServiceCollection services, BffOptions bff)
    {
        // Service discovery resolves "tenancy" (or everything runs in one process); never a hard-coded host.
        services.AddHttpClient(HttpClientName, http => http.BaseAddress = new Uri(bff.AllInOneAddress ?? $"http://{PlatformServices.Tenancy.Key}"));
        return services;
    }

    public static void Map(IEndpointRouteBuilder app)
    {
        var op = app.MapGroup("/bff/operator").AllowAnonymous();
        op.MapPost("/login", LoginAsync);
        op.MapPost("/logout", (HttpContext http) =>
        {
            if (!IsOperatorHost(http))
                return Results.NotFound();
            http.Response.Cookies.Delete(CookieName, CookieOptions(null));
            return Results.NoContent();
        });
    }

    /// <summary>The token from the operator cookie, or null when there is none (or it can't be read any more).</summary>
    public static string? ReadToken(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value))
            return null;
        try
        {
            return Protector(context).Unprotect(value);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private static async Task<IResult> LoginAsync(HttpContext http, [FromBody] JsonElement body, IHttpClientFactory clients)
    {
        if (!IsOperatorHost(http))
            return Results.NotFound();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{PlatformServices.Tenancy.RoutePrefix}/v1/operator/sessions") { Content = JsonContent.Create(body) };
        // The Tenancy service rate-limits sign-in by address: pass the browser's on.
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", http.Connection.RemoteIpAddress?.ToString());
        using var response = await clients.CreateClient(HttpClientName).SendAsync(request, http.RequestAborted);
        if (!response.IsSuccessStatusCode)
            return Results.Content(await response.Content.ReadAsStringAsync(http.RequestAborted), "application/problem+json", statusCode: (int)response.StatusCode);

        var session = await response.Content.ReadFromJsonAsync<Session>(JsonSerializerOptions.Web, http.RequestAborted)
            ?? throw new InvalidOperationException("The operator sign-in returned no session.");
        http.Response.Cookies.Append(CookieName, Protector(http).Protect(session.Token), CookieOptions(session.ExpiresOn));
        return Results.NoContent();
    }

    private static CookieOptions CookieOptions(DateTime? expires) =>
        new() { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", Expires = expires };

    private static IDataProtector Protector(HttpContext context) =>
        context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);

    private sealed record Session(string Token, DateTime ExpiresOn);
}
