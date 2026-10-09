using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Acme.Core.Security;

internal sealed class HttpCurrentPrincipal(IHttpContextAccessor accessor) : ICurrentPrincipal
{
    public ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? Id => IsAuthenticated ? Principal!.FindFirstValue(PlatformClaims.Subject) : null;

    public PrincipalType? Type => IsAuthenticated ? ClaimsPrincipalExtensions.GetPrincipalType(Principal!) : null;

    public string? ActorId => IsAuthenticated ? ClaimsPrincipalExtensions.GetActorId(Principal!) : null;
}

public static class ClaimsPrincipalExtensions
{
    public static PrincipalType? GetPrincipalType(this ClaimsPrincipal principal) =>
        Enum.TryParse<PrincipalType>(principal.FindFirstValue(PlatformClaims.PrincipalType), ignoreCase: true, out var type)
            ? type
            : null;

    public static Guid? GetTenantId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(PlatformClaims.TenantId), out var id) ? id : null;

    /// <summary>The <c>sub</c> inside the <c>act</c> claim (RFC 8693), if any.</summary>
    public static string? GetActorId(this ClaimsPrincipal principal)
    {
        var act = principal.FindFirstValue(PlatformClaims.Actor);
        if (string.IsNullOrEmpty(act))
            return null;
        try
        {
            using var document = JsonDocument.Parse(act);
            return document.RootElement.TryGetProperty(PlatformClaims.Subject, out var sub) ? sub.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool HasPermission(this ClaimsPrincipal principal, string permission) =>
        principal.FindAll(PlatformClaims.Permissions).Any(c => c.Value == permission);
}
