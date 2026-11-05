using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Saaskit.Tenancy.Domain;
using Saaskit.Tenancy.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Saaskit.Integration.Tests.OperatorApi;

namespace Saaskit.Integration.Tests;

/// <summary>The Sign-in providers page's API: settings per provider, secrets that are never returned, and linked accounts.</summary>
[Trait("service", "tenancy")]
public sealed class IdentityProviderTests(Databases databases)
{
    private const string Secret = "s3cr3t-value-that-must-never-come-back";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<List<JsonElement>> ListAsync(HttpClient op) =>
        [.. (await Read(await op.GetAsync($"{Api}/identity-providers", Ct))).EnumerateArray()];

    private static async Task<Guid> UserAsync(HttpClient op, string email) =>
        (await Read(await op.PostAsJsonAsync($"{Api}/principals", new { type = "user", displayName = email, email }, Ct), HttpStatusCode.Created)).GetProperty("principalId").GetGuid();

    [Fact]
    public async Task One_disabled_row_per_kind_is_seeded_and_the_callback_url_comes_from_the_sign_in_host()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await SignInAsync(host);

        var rows = await ListAsync(op);
        rows.Select(r => r.GetProperty("providerKey").GetString()).ShouldBe(["google", "microsoft", "apple", "facebook"]);
        rows.Select(r => r.GetProperty("providerType").GetString()).ShouldBe(["google", "microsoft", "apple", "facebook"]);
        rows.ShouldAllBe(r => !r.GetProperty("isEnabled").GetBoolean() && !r.GetProperty("hasSecret").GetBoolean() && r.GetProperty("linkedAccounts").GetInt32() == 0);
        rows[1].GetProperty("callbackUrl").GetString().ShouldBe("https://login.localhost/account/external/microsoft/callback");
    }

    [Fact]
    public async Task Secrets_are_never_returned_or_logged_and_a_provider_cannot_be_enabled_until_it_is_complete()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await SignInAsync(host);
        var google = (await ListAsync(op))[0];
        var id = google.GetProperty("identityProviderId").GetInt32();

        // Nothing to enable yet.
        var empty = await Read(await PutWithETagAsync(op, $"{Api}/identity-providers/{id}/enabled", new { enabled = true }, google.GetProperty("eTag").GetString()), HttpStatusCode.UnprocessableEntity);
        empty.GetProperty("code").GetString().ShouldBe("tenant-settings.provider-incomplete");

        // A client ID alone is not enough: the secret is needed too.
        var withId = await PutWithETagAsync(op, $"{Api}/identity-providers/{id}", new { displayName = "Google", clientId = "client-1.apps.example.com" }, google.GetProperty("eTag").GetString());
        var idOnly = await Read(withId);
        idOnly.GetProperty("hasSecret").GetBoolean().ShouldBeFalse();
        (await PutWithETagAsync(op, $"{Api}/identity-providers/{id}/enabled", new { enabled = true }, ETag(withId))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var withSecret = await PutWithETagAsync(op, $"{Api}/identity-providers/{id}", new { displayName = "Google", clientId = "client-1.apps.example.com", clientSecret = Secret }, ETag(withId));
        var saved = await withSecret.Content.ReadAsStringAsync(Ct);
        withSecret.StatusCode.ShouldBe(HttpStatusCode.OK, saved);
        saved.ShouldNotContain(Secret);
        JsonDocument.Parse(saved).RootElement.GetProperty("hasSecret").GetBoolean().ShouldBeTrue();

        var enabled = await PutWithETagAsync(op, $"{Api}/identity-providers/{id}/enabled", new { enabled = true }, ETag(withSecret));
        enabled.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Leaving the secret blank keeps the stored one; clearing the client ID of an enabled provider is refused.
        var renamed = await PutWithETagAsync(op, $"{Api}/identity-providers/{id}", new { displayName = "Google (work)", clientId = "client-1.apps.example.com", clientSecret = "" }, ETag(enabled));
        var kept = await Read(renamed);
        kept.GetProperty("hasSecret").GetBoolean().ShouldBeTrue();
        kept.GetProperty("displayName").GetString().ShouldBe("Google (work)");
        (await PutWithETagAsync(op, $"{Api}/identity-providers/{id}", new { displayName = "Google (work)", clientId = "" }, ETag(renamed))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // Stale and missing ETags.
        (await PutWithETagAsync(op, $"{Api}/identity-providers/{id}", new { displayName = "Again", clientId = "client-1.apps.example.com" }, ETag(withId))).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await PutWithETagAsync(op, $"{Api}/identity-providers/{id}/enabled", new { enabled = false }, null)).StatusCode.ShouldBe((HttpStatusCode)428);

        // No response and no log line holds the secret; the stored value is encrypted.
        (await op.GetStringAsync($"{Api}/identity-providers", Ct)).ShouldNotContain(Secret);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenancyCatalogDbContext>();
        var stored = await db.IdentityProviders.AsNoTracking().SingleAsync(p => p.IdentityProviderId == id, Ct);
        stored.ClientSecretProtected.ShouldNotBeNullOrEmpty();
        stored.ClientSecretProtected.ShouldNotContain(Secret);
        var audit = await db.AuditLog.AsNoTracking().Where(a => a.Kind.StartsWith("identity-provider")).ToListAsync(Ct);
        audit.Select(a => a.Kind).ShouldContain("identity-provider-enabled");
        audit.Select(a => a.Kind).ShouldContain("identity-provider-changed");
        audit.ShouldAllBe(a => a.Details == null || !a.Details.Contains(Secret));
        audit.ShouldAllBe(a => a.Actor != null);
    }

    [Fact]
    public async Task Each_kind_needs_its_own_settings_before_it_can_be_enabled()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await SignInAsync(host);

        // Microsoft needs the authority, Apple the team and key IDs; settings of another kind are dropped.
        var microsoft = await op.PostAsJsonAsync($"{Api}/identity-providers", new { providerType = "microsoft", providerKey = "microsoft-acme", displayName = "Microsoft (Acme)", clientId = "app-1", clientSecret = Secret, appleTeamId = "ABCDE12345" }, Ct);
        var acme = await Read(microsoft, HttpStatusCode.Created);
        acme.GetProperty("isEnabled").GetBoolean().ShouldBeFalse();
        acme.GetProperty("appleTeamId").ValueKind.ShouldBe(JsonValueKind.Null);
        acme.GetProperty("callbackUrl").GetString().ShouldBe("https://login.localhost/account/external/microsoft-acme/callback");
        var acmeId = acme.GetProperty("identityProviderId").GetInt32();
        (await PutWithETagAsync(op, $"{Api}/identity-providers/{acmeId}/enabled", new { enabled = true }, ETag(microsoft))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var withAuthority = await PutWithETagAsync(op, $"{Api}/identity-providers/{acmeId}", new { displayName = "Microsoft (Acme)", clientId = "app-1", authority = "organizations" }, ETag(microsoft));
        await Read(withAuthority);
        (await PutWithETagAsync(op, $"{Api}/identity-providers/{acmeId}/enabled", new { enabled = true }, ETag(withAuthority))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var apple = (await ListAsync(op)).Single(r => r.GetProperty("providerKey").GetString() == "apple");
        var appleId = apple.GetProperty("identityProviderId").GetInt32();
        var partial = await PutWithETagAsync(op, $"{Api}/identity-providers/{appleId}", new { displayName = "Apple", clientId = "com.example.login", clientSecret = "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----", appleTeamId = "ABCDE12345" }, apple.GetProperty("eTag").GetString());
        await Read(partial);
        (await PutWithETagAsync(op, $"{Api}/identity-providers/{appleId}/enabled", new { enabled = true }, ETag(partial))).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var complete = await PutWithETagAsync(op, $"{Api}/identity-providers/{appleId}", new { displayName = "Apple", appleTeamId = "ABCDE12345", appleKeyId = "KEY1234567", clientId = "com.example.login" }, ETag(partial));
        await Read(complete);
        (await PutWithETagAsync(op, $"{Api}/identity-providers/{appleId}/enabled", new { enabled = true }, ETag(complete))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Bad input: a duplicate key, an unknown kind, an invalid key and an Apple ID of the wrong length.
        (await op.PostAsJsonAsync($"{Api}/identity-providers", new { providerType = "google", providerKey = "microsoft-acme", displayName = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var bad = await Read(await op.PostAsJsonAsync($"{Api}/identity-providers", new { providerType = "apple", providerKey = "Bad Key", displayName = "", appleTeamId = "123" }, Ct), HttpStatusCode.BadRequest);
        bad.GetProperty("errors").EnumerateObject().Select(e => e.Name).Order().ShouldBe(["appleTeamId", "displayName", "providerKey"]);
    }

    [Fact]
    public async Task One_user_can_link_accounts_across_settings_and_settings_with_accounts_cannot_be_deleted()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await SignInAsync(host);
        var userId = await UserAsync(op, "multi@example.com");
        var otherId = await UserAsync(op, "other@example.com");
        var second = await Read(await op.PostAsJsonAsync($"{Api}/identity-providers", new { providerType = "microsoft", providerKey = "microsoft-acme", displayName = "Microsoft (Acme)" }, Ct), HttpStatusCode.Created);
        var secondId = (short)second.GetProperty("identityProviderId").GetInt32();
        var seeded = await ListAsync(op);
        short IdOf(string key) => (short)seeded.Single(r => r.GetProperty("providerKey").GetString() == key).GetProperty("identityProviderId").GetInt32();

        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenancyCatalogDbContext>();
        // Across kinds, and across two Microsoft settings rows with the same subject (an ID is only unique within one app).
        db.PrincipalUserProviders.AddRange(
            new PrincipalUserProvider(userId, IdOf("google"), "g-1", "multi@gmail.com", true, now),
            new PrincipalUserProvider(userId, IdOf("microsoft"), "m-1", "multi@work.example", true, now),
            new PrincipalUserProvider(userId, secondId, "m-1", null, false, now),
            new PrincipalUserProvider(userId, IdOf("facebook"), "f-1", null, false, now));
        await db.SaveChangesAsync(Ct);

        var detail = await Read(await op.GetAsync($"{Api}/principals/{userId}", Ct));
        var linked = detail.GetProperty("providers").EnumerateArray().ToList();
        linked.Select(p => p.GetProperty("providerKey").GetString()).ShouldBe(["google", "microsoft", "microsoft-acme", "facebook"]);
        linked.ShouldAllBe(p => p.GetProperty("principalUserProviderId").GetInt64() > 0);
        linked[3].GetProperty("providerType").GetString().ShouldBe("facebook");
        (await ListAsync(op)).Single(r => r.GetProperty("providerKey").GetString() == "microsoft-acme").GetProperty("linkedAccounts").GetInt32().ShouldBe(1);

        // The same account of the same settings can't be linked to a second user.
        var clash = db.PrincipalUserProviders.Add(new PrincipalUserProvider(otherId, secondId, "m-1", null, false, now));
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        clash.State = EntityState.Detached;

        // Settings with linked accounts can only be disabled.
        var deleteRefused = await Read(await op.DeleteAsync($"{Api}/identity-providers/{secondId}", Ct), HttpStatusCode.Conflict);
        deleteRefused.GetProperty("code").GetString().ShouldBe("tenant-settings.provider-has-accounts");

        // Unlinking needs the right user, then the settings can go.
        var link = linked[2].GetProperty("principalUserProviderId").GetInt64();
        (await op.DeleteAsync($"{Api}/principals/{otherId}/providers/{link}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await op.DeleteAsync($"{Api}/principals/{userId}/providers/{link}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await op.DeleteAsync($"{Api}/principals/{userId}/providers/{link}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Read(await op.GetAsync($"{Api}/principals/{userId}", Ct))).GetProperty("providers").GetArrayLength().ShouldBe(3);
        (await op.DeleteAsync($"{Api}/identity-providers/{secondId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(op)).Count.ShouldBe(4);

        db.ChangeTracker.Clear();
        var kinds = await db.AuditLog.AsNoTracking().Select(a => a.Kind).ToListAsync(Ct);
        kinds.ShouldContain("principal-provider-unlinked");
        kinds.ShouldContain("identity-provider-deleted");
    }
}
