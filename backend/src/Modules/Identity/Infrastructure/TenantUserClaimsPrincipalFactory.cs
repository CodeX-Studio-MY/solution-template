using System.Security.Claims;
using Saaskit.Core.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Saaskit.Identity.Infrastructure;

/// <summary>Adds the tenant's slug to the sign-in cookie, so the authorize endpoint knows which tenant the user signed in to.</summary>
internal sealed class TenantUserClaimsPrincipalFactory(
    UserManager<PlatformUser> users,
    IOptions<IdentityOptions> options,
    ITenantContext tenant) : UserClaimsPrincipalFactory<PlatformUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(PlatformUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(IdentityModuleSetup.TenantCookieClaim, tenant.Tenant.Slug));
        return identity;
    }
}
