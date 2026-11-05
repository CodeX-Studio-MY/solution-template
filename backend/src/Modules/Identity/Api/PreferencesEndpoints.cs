using Saaskit.Core.Authorization;
using Saaskit.Core.Errors;
using Saaskit.Core.Security;
using Saaskit.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Saaskit.Identity.Api;

/// <summary>The signed-in user's own preferences. Any signed-in user, in no particular module; machines and services have none.</summary>
internal static class PreferencesEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var preferences = api.MapGroup("/v1/me/preferences").WithTags("preferences");
        preferences.MapGet("", (ICurrentPrincipal who, IUserPreferences service, CancellationToken ct) =>
            service.GetAsync(UserId(who), ct)).AllowPrincipalTypes(PrincipalType.User);
        // A support session (or any delegated token) acts for the customer: it must not change the customer's choices.
        preferences.MapPut("", (ICurrentPrincipal who, [FromBody] PreferencesBody body, IUserPreferences service, CancellationToken ct) =>
            who.ActorId is not null
                ? throw new ForbiddenException(IdentityErrors.PreferencesNotForSupport)
                : service.SetLocaleAsync(UserId(who), body.Locale, ct)).AllowPrincipalTypes(PrincipalType.User);
    }

    private static Guid UserId(ICurrentPrincipal who) =>
        Guid.TryParse(who.Id, out var id) ? id : throw new ForbiddenException();

    internal sealed record PreferencesBody(string? Locale);
}
