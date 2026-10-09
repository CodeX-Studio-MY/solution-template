using System.Security.Cryptography;
using System.Text;
using Acme.Core.Configuration;
using Acme.Core.Errors;
using Acme.Core.Platform;
using Acme.Core.Security;
using Acme.Core.Tenancy;
using Acme.Identity.Application;
using Acme.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Acme.Identity.Infrastructure;

internal sealed class AccountService(
    IdentityDbContext db,
    UserManager<PlatformUser> users,
    ITenantContext tenant,
    IOptions<TenancyOptions> tenancy,
    IOptions<TokenServerOptions> tokenServer,
    IAccountMessages messages,
    IAuditWriter audit,
    TimeProvider time) : IAccountService
{
    public async Task<InvitationResult> InviteAsync(string email, string displayName, IEnumerable<(string Module, Role Role)> roles, CancellationToken cancellationToken = default)
    {
        var normalised = email.Trim().ToUpperInvariant();
        if (await db.Invitations.AnyAsync(i => i.NormalizedEmail == normalised && i.AcceptedOn == null, cancellationToken)
            || await users.Users.AnyAsync(u => u.NormalizedEmail == normalised, cancellationToken))
            throw new ConflictException(IdentityErrors.EmailInUse);

        var now = time.GetUtcNow().UtcDateTime;
        var principal = Principal.NewUser(displayName);
        principal.Disable();   // inactive until the invitation is accepted
        db.Principals.Add(principal);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var (module, role) in roles)
        {
            var moduleId = Modules.All.SingleOrDefault(m => m.Key == module)?.Id
                ?? throw new BusinessRuleException(IdentityErrors.UnknownModule, module);
            db.AccessAssignments.Add(AccessAssignment.ForPrincipal(principal.Id, moduleId, role));
        }

        var token = Token();
        var expires = now.AddHours(tokenServer.Value.InvitationHours);
        db.Invitations.Add(new Invitation(email.Trim(), Hash(token), expires, principal.Id));
        await audit.WriteAsync(AuditKinds.InvitationCreated, subject: principal.Id.ToString(), details: new { email = email.Trim() }, saveNow: false, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var url = new Uri($"{tenancy.Value.Issuer}account/invitation?tenant={Uri.EscapeDataString(tenant.Tenant.Slug)}&token={token}");
        await messages.SendAsync(new AccountLink("invitation", email.Trim(), displayName, url, expires), cancellationToken);
        return new InvitationResult(principal.Id, url, expires);
    }

    public async Task<bool> IsInvitationValidAsync(string token, CancellationToken cancellationToken = default)
    {
        var hash = Hash(token);
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        return invitation?.CanAccept(time.GetUtcNow().UtcDateTime) == true;
    }

    public async Task<AccountOutcome> AcceptInvitationAsync(string token, string password, CancellationToken cancellationToken = default)
    {
        var hash = Hash(token);
        var now = time.GetUtcNow().UtcDateTime;
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        if (invitation is null || !invitation.CanAccept(now))
            return AccountOutcome.Fail("This invitation is not valid any more. Ask for a new one.");

        var principal = await db.Principals.SingleAsync(p => p.Id == invitation.PrincipalId, cancellationToken);
        var user = new PlatformUser
        {
            Id = principal.Id,
            UserName = invitation.Email,
            Email = invitation.Email,
            EmailConfirmed = true,   // the invitation went to this address
            SecurityStamp = Guid.NewGuid().ToString("N"),
        };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
            return AccountOutcome.Fail([.. created.Errors.Select(e => e.Description)]);

        invitation.Accept(now);
        principal.Enable();
        await audit.WriteAsync(AuditKinds.InvitationAccepted, subject: principal.Id.ToString(), actorId: principal.Id.ToString(), actorType: PrincipalType.User.ToClaimValue(), saveNow: false, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return AccountOutcome.Ok(principal.Id);
    }

    public async Task RequestPasswordResetAsync(string userNameOrEmail, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByNameAsync(userNameOrEmail) ?? await users.FindByEmailAsync(userNameOrEmail);
        if (user is null || user.Email is null)
            return;
        var principal = await db.Principals.SingleAsync(p => p.Id == user.Id, cancellationToken);
        if (!principal.IsActive)
            return;

        // Single use: the token is bound to the security stamp, which changes when the password does.
        var resetToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
        var url = new Uri($"{tenancy.Value.Issuer}account/reset?tenant={Uri.EscapeDataString(tenant.Tenant.Slug)}&uid={user.Id}&token={resetToken}");
        await messages.SendAsync(new AccountLink("password-reset", user.Email, principal.DisplayName, url, time.GetUtcNow().UtcDateTime.AddHours(1)), cancellationToken);
        await audit.WriteAsync(AuditKinds.PasswordReset, subject: user.Id.ToString(), details: new { stage = "requested" }, cancellationToken: cancellationToken);
    }

    public async Task<AccountOutcome> ResetPasswordAsync(string userId, string token, string password, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null)
            return AccountOutcome.Fail("This reset link is not valid any more.");
        string raw;
        try
        {
            raw = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            return AccountOutcome.Fail("This reset link is not valid any more.");
        }

        var result = await users.ResetPasswordAsync(user, raw, password);
        if (!result.Succeeded)
            return AccountOutcome.Fail([.. result.Errors.Select(e => e.Description)]);

        user.ConsecutiveLockouts = 0;
        await users.SetLockoutEndDateAsync(user, null);
        await users.UpdateAsync(user);
        var principal = await db.Principals.SingleAsync(p => p.Id == user.Id, cancellationToken);
        principal.BumpAccessVersion();   // existing sessions must refresh
        await audit.WriteAsync(AuditKinds.PasswordReset, subject: user.Id.ToString(), details: new { stage = "completed" }, actorId: user.Id.ToString(), actorType: PrincipalType.User.ToClaimValue(), cancellationToken: cancellationToken);
        return AccountOutcome.Ok(user.Id);
    }

    private static string Token() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    internal static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
