using System.Net;
using System.Net.Http.Json;
using Saaskit.Tenancy.Application;
using Saaskit.Tenancy.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Saaskit.Integration.Tests;

/// <summary>Recovering an operator who lost their details, and checking saved details without counting a failed sign-in.</summary>
[Trait("service", "tenancy")]
public sealed class OperatorAccountTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> SignIn(HttpClient client, string email, string password, string secret) =>
        client.PostAsJsonAsync("/api/tenancy/v1/operator/sessions",
            new { email, password, totpCode = Totp.Compute(secret, TimeProvider.System.GetUtcNow().ToUnixTimeSeconds() / 30) }, Ct);

    [Fact]
    public async Task A_reset_replaces_both_factors_and_the_check_tells_old_from_new()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IOperatorAccounts>();
        var first = await accounts.CreateAsync("lost@example.com", "Lost", Ct);
        (await accounts.CredentialsMatchAsync("lost@example.com", first.Password, first.TotpSecret, Ct)).ShouldBeTrue();

        var reset = await accounts.ResetCredentialsAsync("lost@example.com", Ct);
        (await accounts.CredentialsMatchAsync("lost@example.com", first.Password, first.TotpSecret, Ct)).ShouldBeFalse();
        (await accounts.CredentialsMatchAsync("lost@example.com", reset.Password, reset.TotpSecret, Ct)).ShouldBeTrue();
        (await accounts.CredentialsMatchAsync("nobody@example.com", reset.Password, reset.TotpSecret, Ct)).ShouldBeFalse();

        var client = host.Client();
        (await SignIn(client, "lost@example.com", first.Password, first.TotpSecret)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SignIn(client, "lost@example.com", reset.Password, reset.TotpSecret)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
