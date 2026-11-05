using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Saaskit.Tenancy.Application;
using Saaskit.Tenancy.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Saaskit.Integration.Tests;

/// <summary>Helpers for tests of the operator console's API: sign an operator in, read JSON, onboard a tenant.</summary>
internal static class OperatorApi
{
    public const string Api = "/api/tenancy/v1/operator";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, body);
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement;
    }

    /// <summary>Creates an operator and returns a client signed in as them (password and one-time code, as the console does).</summary>
    public static async Task<HttpClient> SignInAsync(TenancyTestHost host, string email = "ops@example.com", string name = "Ops One")
    {
        await using var scope = host.Services.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<IOperatorAccounts>().CreateAsync(email, name, Ct);
        var client = host.Client();
        var code = Totp.Compute(created.TotpSecret, TimeProvider.System.GetUtcNow().ToUnixTimeSeconds() / 30);
        var session = await Read(await client.PostAsJsonAsync($"{Api}/sessions", new { email, password = created.Password, totpCode = code }, Ct));
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Operator {session.GetProperty("token").GetString()}");
        return client;
    }

    public static async Task<Guid> OnboardAsync(HttpClient op, string slug, string plan) =>
        (await Read(await op.PostAsJsonAsync($"{Api}/tenants", new { slug, displayName = slug, plan, adminEmail = $"a@{slug}.example.com", adminName = "A" }, Ct), HttpStatusCode.Created))
            .GetProperty("tenantId").GetGuid();

    /// <summary>A PUT carrying an <c>If-Match</c> (the plan's or provider's ETag from the last read).</summary>
    public static Task<HttpResponseMessage> PutWithETagAsync(HttpClient client, string path, object body, string? eTag)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        if (eTag is not null)
            request.Headers.TryAddWithoutValidation("If-Match", eTag);
        return client.SendAsync(request, Ct);
    }

    public static string ETag(HttpResponseMessage response) => response.Headers.ETag?.ToString() ?? "";
}
