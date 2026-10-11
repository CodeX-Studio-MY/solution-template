using System.Security.Claims;
using Acme.Core.Configuration;
using Acme.Core.Security;
using Acme.Core.Tenancy;
using Acme.Identity.Application;
using Acme.Identity.Domain;
using Acme.Identity.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Acme.Identity.Api;

/// <summary>
/// The sign-in pages on <c>login.{BaseDomain}</c>: password sign-in with lockout that grows, TOTP, passkeys, invitation
/// acceptance, and password reset. They authenticate against the tenant named by the <c>tenant</c> parameter.
/// </summary>
internal static class AccountEndpoints
{
    private const int MaxLockoutMinutes = 24 * 60;

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/account/style.css", () => Results.Content(Html.Css, "text/css")).AllowAnonymous();
        app.MapGet("/account/passkey.js", () => Results.Content(PasskeyScript.Source, "text/javascript")).AllowAnonymous();

        var account = app.MapGroup("/account").AllowAnonymous().WithMetadata(TenantFromParameter.Instance);
        account.MapGet("/login", LoginPage);
        account.MapPost("/login", Login).WithMetadata(AuthRateLimited.Instance);
        account.MapGet("/mfa", MfaPage);
        account.MapPost("/mfa", Mfa).WithMetadata(AuthRateLimited.Instance);
        account.MapGet("/invitation", InvitationPage);
        account.MapPost("/invitation", AcceptInvitation).WithMetadata(AuthRateLimited.Instance);
        account.MapGet("/forgot", ForgotPage);
        account.MapPost("/forgot", Forgot).WithMetadata(AuthRateLimited.Instance);
        account.MapGet("/reset", ResetPage);
        account.MapPost("/reset", Reset).WithMetadata(AuthRateLimited.Instance);
        account.MapPost("/passkey/request-options", PasskeyRequestOptions).WithMetadata(AuthRateLimited.Instance);
        account.MapPost("/passkey/signin", PasskeySignIn).WithMetadata(AuthRateLimited.Instance);

        var signedIn = app.MapGroup("/account").WithMetadata(TenantFromParameter.Instance);
        signedIn.MapGet("/security", SecurityPage);
        signedIn.MapPost("/security/totp", EnableTotp);
        signedIn.MapPost("/passkey/creation-options", PasskeyCreationOptions);
        signedIn.MapPost("/passkey/register", PasskeyRegister);
    }

    // ---- Sign in -------------------------------------------------------------------------------

    private static async Task<IResult> LoginPage(HttpContext http, ITenantContext tenant, IAntiforgery antiforgery, string? returnUrl) =>
        Html.Page("Sign in", tenant.Tenant.DisplayName, LoginForm(http, tenant, antiforgery, returnUrl, null), script: "/account/passkey.js");

    private static string LoginForm(HttpContext http, ITenantContext tenant, IAntiforgery antiforgery, string? returnUrl, IEnumerable<string>? errors) => $"""
        {Html.Errors(errors)}
        <form method="post" action="/account/login">
        {Csrf(http, antiforgery)}{Html.Hidden("tenant", tenant.Tenant.Slug)}{Html.Hidden("returnUrl", returnUrl)}
        {Html.Field("username", "Email", autocomplete: "username webauthn")}
        {Html.Field("password", "Password", "password", autocomplete: "current-password")}
        <p><button type="submit">Sign in</button></p>
        </form>
        <p><button type="button" id="passkey" class="secondary" data-mode="request">Use a passkey</button></p>
        <p id="passkey-error" role="alert"></p>
        <p><a href="/account/forgot?tenant={Uri.EscapeDataString(tenant.Tenant.Slug)}&amp;returnUrl={Uri.EscapeDataString(returnUrl ?? "")}">Forgot your password?</a></p>
        """;

    private static async Task<IResult> Login(
        HttpContext http,
        ITenantContext tenant,
        IAntiforgery antiforgery,
        SignInManager<PlatformUser> signIn,
        UserManager<PlatformUser> users,
        IdentityDbContext db,
        IAuditWriter audit,
        IOptions<TenancyOptions> tenancy,
        TimeProvider time)
    {
        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        var returnUrl = form["returnUrl"].ToString();
        await antiforgery.ValidateRequestAsync(http);

        var userName = form["username"].ToString().Trim();
        var user = await users.FindByNameAsync(userName);
        var result = user is null
            ? SignInResult.Failed
            : await signIn.PasswordSignInAsync(user, form["password"].ToString(), isPersistent: false, lockoutOnFailure: true);

        if (result.Succeeded && user is not null)
        {
            if (!await db.Principals.AnyAsync(p => p.Id == user.Id && p.IsActive, http.RequestAborted))
            {
                await signIn.SignOutAsync();
                result = SignInResult.NotAllowed;
            }
            else
            {
                user.ConsecutiveLockouts = 0;
                await users.UpdateAsync(user);
                await audit.WriteAsync(AuditKinds.SignIn, subject: user.Id.ToString(), actorId: user.Id.ToString(), actorType: PrincipalType.User.ToClaimValue(), cancellationToken: http.RequestAborted);
                return Results.Redirect(AfterSignIn(returnUrl, tenant.Tenant.Slug, tenancy.Value));
            }
        }

        if (result.RequiresTwoFactor)
            return Results.Redirect($"/account/mfa?tenant={Uri.EscapeDataString(tenant.Tenant.Slug)}&returnUrl={Uri.EscapeDataString(returnUrl)}");

        if (result.IsLockedOut && user is not null)
            await ExtendLockoutAsync(users, user, time);
        await audit.WriteAsync(AuditKinds.SignInFailed, subject: user?.Id.ToString(), details: new { lockedOut = result.IsLockedOut }, actorId: user?.Id.ToString() ?? "unknown", actorType: PrincipalType.User.ToClaimValue(), cancellationToken: http.RequestAborted);

        // One message for every failure, so accounts can't be enumerated.
        var message = result.IsLockedOut ? "Too many attempts. Try again later." : "The email or password is not right.";
        return Html.Page("Sign in", tenant.Tenant.DisplayName, LoginForm(http, tenant, antiforgery, returnUrl, [message]), script: "/account/passkey.js", status: StatusCodes.Status400BadRequest);
    }

    /// <summary>Lockouts grow: 1, 2, 4, 8 … minutes up to a day. Never permanent, so attackers can't lock users out for good.</summary>
    private static async Task ExtendLockoutAsync(UserManager<PlatformUser> users, PlatformUser user, TimeProvider time)
    {
        user.ConsecutiveLockouts++;
        var minutes = Math.Min(MaxLockoutMinutes, Math.Pow(2, Math.Min(user.ConsecutiveLockouts - 1, 11)));
        await users.SetLockoutEndDateAsync(user, time.GetUtcNow().AddMinutes(minutes));
        await users.UpdateAsync(user);
    }

    // ---- Second factor -------------------------------------------------------------------------

    private static IResult MfaPage(HttpContext http, ITenantContext tenant, IAntiforgery antiforgery, string? returnUrl) =>
        Html.Page("Verification code", tenant.Tenant.DisplayName, MfaForm(http, tenant, antiforgery, returnUrl, null));

    private static string MfaForm(HttpContext http, ITenantContext tenant, IAntiforgery antiforgery, string? returnUrl, IEnumerable<string>? errors) => $"""
        {Html.Errors(errors)}
        <form method="post" action="/account/mfa">
        {Csrf(http, antiforgery)}{Html.Hidden("tenant", tenant.Tenant.Slug)}{Html.Hidden("returnUrl", returnUrl)}
        {Html.Field("code", "Code from your authenticator app", autocomplete: "one-time-code")}
        <p><button type="submit">Verify</button></p>
        </form>
        """;

    private static async Task<IResult> Mfa(HttpContext http, ITenantContext tenant, IAntiforgery antiforgery, SignInManager<PlatformUser> signIn, IAuditWriter audit, IOptions<TenancyOptions> tenancy)
    {
        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        await antiforgery.ValidateRequestAsync(http);
        var returnUrl = form["returnUrl"].ToString();
        var code = form["code"].ToString().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);

        var result = await signIn.TwoFactorAuthenticatorSignInAsync(code, isPersistent: false, rememberClient: false);
        if (result.Succeeded)
        {
            await audit.WriteAsync(AuditKinds.SignIn, details: new { mfa = true }, cancellationToken: http.RequestAborted);
            return Results.Redirect(AfterSignIn(returnUrl, tenant.Tenant.Slug, tenancy.Value));
        }
        await audit.WriteAsync(AuditKinds.SignInFailed, details: new { mfa = true, lockedOut = result.IsLockedOut }, cancellationToken: http.RequestAborted);
        return Html.Page("Verification code", tenant.Tenant.DisplayName, MfaForm(http, tenant, antiforgery, returnUrl, ["That code is not right."]), status: StatusCodes.Status400BadRequest);
    }

    // ---- Passkeys ------------------------------------------------------------------------------

    private static async Task<IResult> PasskeyRequestOptions(HttpContext http, SignInManager<PlatformUser> signIn)
    {
        await ValidateHeaderCsrfAsync(http);
        // No user yet: the browser offers the passkeys it holds for this site (discoverable credentials).
        var options = await signIn.MakePasskeyRequestOptionsAsync(user: null);
        return Results.Content(options, "application/json");
    }

    private static async Task<IResult> PasskeySignIn(HttpContext http, ITenantContext tenant, IOptions<TenancyOptions> tenancy, SignInManager<PlatformUser> signIn, UserManager<PlatformUser> users, IdentityDbContext db, IAuditWriter audit)
    {
        await ValidateHeaderCsrfAsync(http);
        var body = await http.Request.ReadFromJsonAsync<PasskeyBody>(http.RequestAborted);
        if (body?.CredentialJson is null)
            return Results.BadRequest();

        var result = await signIn.PasskeySignInAsync(body.CredentialJson);
        if (!result.Succeeded)
        {
            await audit.WriteAsync(AuditKinds.SignInFailed, details: new { passkey = true }, cancellationToken: http.RequestAborted);
            return Results.Unauthorized();
        }

        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        await audit.WriteAsync(AuditKinds.SignIn, subject: userId, actorId: userId, actorType: PrincipalType.User.ToClaimValue(), details: new { passkey = true }, cancellationToken: http.RequestAborted);
        return Results.Json(new { redirect = AfterSignIn(body.ReturnUrl, tenant.Tenant.Slug, tenancy.Value) });
    }

    private static async Task<IResult> PasskeyCreationOptions(HttpContext http, SignInManager<PlatformUser> signIn, UserManager<PlatformUser> users)
    {
        await ValidateHeaderCsrfAsync(http);
        var user = await users.GetUserAsync(http.User);
        if (user is null)
            return Results.Unauthorized();
        var options = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = user.Id.ToString(),
            Name = user.UserName ?? user.Id.ToString(),
            DisplayName = user.UserName ?? user.Id.ToString(),
        });
        return Results.Content(options, "application/json");
    }

    private static async Task<IResult> PasskeyRegister(HttpContext http, SignInManager<PlatformUser> signIn, UserManager<PlatformUser> users, IAuditWriter audit)
    {
        await ValidateHeaderCsrfAsync(http);
        var user = await users.GetUserAsync(http.User);
        var body = await http.Request.ReadFromJsonAsync<PasskeyBody>(http.RequestAborted);
        if (user is null || body?.CredentialJson is null)
            return Results.BadRequest();

        var attestation = await signIn.PerformPasskeyAttestationAsync(body.CredentialJson);
        if (!attestation.Succeeded)
            return Results.BadRequest();
        var saved = await users.AddOrUpdatePasskeyAsync(user, attestation.Passkey);
        if (!saved.Succeeded)
            return Results.BadRequest();
        await audit.WriteAsync(AuditKinds.PrincipalChanged, subject: user.Id.ToString(), details: new { passkeyAdded = true }, cancellationToken: http.RequestAborted);
        return Results.Json(new { redirect = "/account/security?tenant=" + Uri.EscapeDataString(http.Request.Query["tenant"].ToString()) });
    }

    // ---- Invitations and password reset --------------------------------------------------------

    private static async Task<IResult> InvitationPage(HttpContext http, ITenantContext tenant, IAntiforgery antiforgery, IAccountService accounts, string? token)
    {
        if (string.IsNullOrEmpty(token) || !await accounts.IsInvitationValidAsync(token, http.RequestAborted))
            return Html.Page("Invitation", tenant.Tenant.DisplayName, "<p role=\"alert\">This invitation is not valid any more. Ask for a new one.</p>", status: StatusCodes.Status400BadRequest);
        return Html.Page("Choose a password", tenant.Tenant.DisplayName, PasswordForm(http, tenant, antiforgery, "/account/invitation", "token", token, null));
    }

    private static async Task<IResult> AcceptInvitation(HttpContext http, ITenantContext tenant, IAntiforgery antiforgery, IAccountService accounts)
    {
        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        await antiforgery.ValidateRequestAsync(http);
        var token = form["token"].ToString();
        var outcome = await accounts.AcceptInvitationAsync(token, form["password"].ToString(), http.RequestAborted);
        if (outcome.Succeeded)
            return Results.Redirect($"/account/login?tenant={Uri.EscapeDataString(tenant.Tenant.Slug)}");
        return Html.Page("Choose a password", tenant.Tenant.DisplayName, PasswordForm(http, tenant, antiforgery, "/account/invitation", "token", token, outcome.Errors), status: StatusCodes.Status400BadRequest);
    }

    private static IResult ForgotPage(HttpContext http, ITenantContext tenant, IAntiforgery antiforgery) =>
        Html.Page("Reset your password", tenant.Tenant.DisplayName, $"""
            <form method="post" action="/account/forgot">
            {Csrf(http, antiforgery)}{Html.Hidden("tenant", tenant.Tenant.Slug)}
            {Html.Field("username", "Email", autocomplete: "username")}
            <p><button type="submit">Send a reset link</button></p>
            </form>
            """);

    private static async Task<IResult> Forgot(HttpContext http, ITenantContext tenant, IAntiforgery antiforgery, IAccountService accounts)
    {
        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        await antiforgery.ValidateRequestAsync(http);
        await accounts.RequestPasswordResetAsync(form["username"].ToString().Trim(), http.RequestAborted);
        // The same answer whether or not the account exists.
        return Html.Page("Check your email", tenant.Tenant.DisplayName, "<p>If that account exists, we sent a link to reset the password.</p>");
    }

    private static IResult ResetPage(HttpContext http, ITenantContext tenant, IAntiforgery antiforgery, string? uid, string? token) =>
        Html.Page("Choose a new password", tenant.Tenant.DisplayName,
            PasswordForm(http, tenant, antiforgery, "/account/reset", "token", token, null, extraName: "uid", extraValue: uid));

    private static async Task<IResult> Reset(HttpContext http, ITenantContext tenant, IAntiforgery antiforgery, IAccountService accounts)
    {
        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        await antiforgery.ValidateRequestAsync(http);
        var outcome = await accounts.ResetPasswordAsync(form["uid"].ToString(), form["token"].ToString(), form["password"].ToString(), http.RequestAborted);
        if (outcome.Succeeded)
            return Results.Redirect($"/account/login?tenant={Uri.EscapeDataString(tenant.Tenant.Slug)}");
        return Html.Page("Choose a new password", tenant.Tenant.DisplayName,
            PasswordForm(http, tenant, antiforgery, "/account/reset", "token", form["token"].ToString(), outcome.Errors, extraName: "uid", extraValue: form["uid"].ToString()),
            status: StatusCodes.Status400BadRequest);
    }

    private static string PasswordForm(HttpContext http, ITenantContext tenant, IAntiforgery antiforgery, string action, string tokenName, string? token, IEnumerable<string>? errors, string? extraName = null, string? extraValue = null) => $"""
        {Html.Errors(errors)}
        <form method="post" action="{action}">
        {Csrf(http, antiforgery)}{Html.Hidden("tenant", tenant.Tenant.Slug)}{Html.Hidden(tokenName, token)}{(extraName is null ? "" : Html.Hidden(extraName, extraValue))}
        {Html.Field("password", "New password", "password", autocomplete: "new-password")}
        <p>Use at least {NistPasswordValidator.MinimumLengthWithoutMfa} characters (or {NistPasswordValidator.MinimumLengthWithMfa} if you turn on two-step verification). A passphrase works well.</p>
        <p><button type="submit">Save password</button></p>
        </form>
        """;

    // ---- Security settings: TOTP and passkeys --------------------------------------------------

    private static async Task<IResult> SecurityPage(HttpContext http, ITenantContext tenant, IAntiforgery antiforgery, UserManager<PlatformUser> users)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null)
            return Results.Challenge(new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = http.Request.Path + http.Request.QueryString }, [IdentityConstants.ApplicationScheme]);

        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            key = await users.GetAuthenticatorKeyAsync(user);
        }
        var issuer = Uri.EscapeDataString(tenant.Tenant.DisplayName);
        var uri = $"otpauth://totp/{issuer}:{Uri.EscapeDataString(user.UserName ?? "")}?secret={key}&issuer={issuer}";
        var enabled = await users.GetTwoFactorEnabledAsync(user);
        var passkeys = (await users.GetPasskeysAsync(user)).Count;

        return Html.Page("Security", tenant.Tenant.DisplayName, $"""
            <h2>Two-step verification</h2>
            <p>{(enabled ? "On." : "Off.")} Add this key to an authenticator app, then enter a code to turn it on.</p>
            <p><code>{Html.E(key)}</code></p>
            <p><a href="{Html.E(uri)}">Open in an authenticator app</a></p>
            <form method="post" action="/account/security/totp">
            {Csrf(http, antiforgery)}{Html.Hidden("tenant", tenant.Tenant.Slug)}
            {Html.Field("code", "Code", autocomplete: "one-time-code")}
            <p><button type="submit">Turn on</button></p>
            </form>
            <h2>Passkeys</h2>
            <p>{passkeys} registered.</p>
            <p><button type="button" id="passkey" data-mode="creation">Add a passkey</button></p>
            <p id="passkey-error" role="alert"></p>
            """, script: "/account/passkey.js");
    }

    private static async Task<IResult> EnableTotp(HttpContext http, ITenantContext tenant, IAntiforgery antiforgery, UserManager<PlatformUser> users, IAuditWriter audit)
    {
        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        await antiforgery.ValidateRequestAsync(http);
        var user = await users.GetUserAsync(http.User);
        if (user is null)
            return Results.Unauthorized();

        var code = form["code"].ToString().Replace(" ", "", StringComparison.Ordinal);
        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
            return Html.Page("Security", tenant.Tenant.DisplayName, "<p role=\"alert\">That code is not right. Go back and try again.</p>", status: StatusCodes.Status400BadRequest);

        await users.SetTwoFactorEnabledAsync(user, true);
        await audit.WriteAsync(AuditKinds.PrincipalChanged, subject: user.Id.ToString(), details: new { totpEnabled = true }, cancellationToken: http.RequestAborted);
        return Results.Redirect($"/account/security?tenant={Uri.EscapeDataString(tenant.Tenant.Slug)}");
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static string Csrf(HttpContext http, IAntiforgery antiforgery) =>
        Html.Hidden(antiforgery.GetAndStoreTokens(http).FormFieldName ?? "__csrf", antiforgery.GetAndStoreTokens(http).RequestToken);

    /// <summary>JSON endpoints send the antiforgery token in a header (the page puts it in a hidden field the script reads).</summary>
    private static async Task ValidateHeaderCsrfAsync(HttpContext http) =>
        await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);

    /// <summary>Only local paths are followed after sign-in, never another site.</summary>
    internal static string SafeLocal(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl[0] == '/' && !returnUrl.StartsWith("//", StringComparison.Ordinal) && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";

    /// <summary>
    /// Where to send a person who has just signed in: the local path they came from (such as the authorize request the Gateway
    /// started), or, when there is none, their tenant's own app. Accepting an invitation or resetting a password arrives here with
    /// no return path, and the central sign-in host has no app of its own to land on.
    /// </summary>
    internal static string AfterSignIn(string? returnUrl, string tenantSlug, TenancyOptions tenancy)
    {
        var local = SafeLocal(returnUrl);
        return local == "/" ? $"{tenancy.TenantOrigin(tenantSlug)}/" : local;
    }

    private sealed record PasskeyBody(string? CredentialJson, string? ReturnUrl);
}
