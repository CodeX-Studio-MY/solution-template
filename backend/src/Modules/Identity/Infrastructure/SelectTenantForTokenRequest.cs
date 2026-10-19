using Saaskit.Core.Tenancy;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace Saaskit.Identity.Infrastructure;

/// <summary>
/// Runs first for every token request and chooses the tenant database that the authorizations and tokens live in.
/// A tenant's own clients (machines, integrations) carry their tenant in the client registration. Platform clients
/// (the BFF and platform services) have no tenant and name it with the <c>tenant</c> parameter.
/// </summary>
internal sealed class SelectTenantForTokenRequest(
    OpenIddict.OidcApplicationStore applications,
    ITenantDirectory directory,
    ITenantContextSetter tenant) : IOpenIddictServerHandler<OpenIddictServerEvents.ValidateTokenRequestContext>
{
    public async ValueTask HandleAsync(OpenIddictServerEvents.ValidateTokenRequestContext context)
    {
        var clientId = context.Request.ClientId ?? ClientIdFromAssertion(context.Request.ClientAssertion);
        var application = clientId is null ? null : await applications.FindByClientIdAsync(clientId, context.CancellationToken);
        if (application is null)
            return;   // OpenIddict reports the unknown client.

        Guid? tenantId = application.TenantId;
        if (tenantId is null && context.Request.GetParameter(IdentityModuleSetup.TenantParameter)?.ToString() is { Length: > 0 } slug)
        {
            var found = await directory.FindBySlugAsync(slug, context.CancellationToken);
            tenantId = found?.Id;
            if (found is null)
            {
                context.Reject(OpenIddictConstants.Errors.InvalidRequest, "The tenant is not known.");
                return;
            }
        }

        if (tenantId is null)
            return;
        var info = await directory.FindByIdAsync(tenantId.Value, context.CancellationToken);
        if (info is null || !info.CanRead)
        {
            context.Reject(OpenIddictConstants.Errors.InvalidClient, "The tenant is not available.");
            return;
        }
        tenant.SetTenant(info);
    }

    /// <summary>The client ID in a <c>private_key_jwt</c> assertion (its <c>sub</c>), read without trusting it: OpenIddict verifies the signature later.</summary>
    private static string? ClientIdFromAssertion(string? assertion)
    {
        if (string.IsNullOrEmpty(assertion))
            return null;
        var handler = new JsonWebTokenHandler();
        return handler.CanReadToken(assertion) ? handler.ReadJsonWebToken(assertion).Subject : null;
    }
}
