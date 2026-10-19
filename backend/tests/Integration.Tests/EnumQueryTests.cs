using System.Globalization;
using Saaskit.Core.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Saaskit.Integration.Tests;

/// <summary>
/// The API documents enums in camelCase (<c>status=draft</c>) and the generated clients send that. ASP.NET alone binds enum
/// query values case-sensitively and answers 400 for it; the platform pipeline accepts the documented form.
/// </summary>
public sealed class EnumQueryTests
{
    private enum Stage
    {
        Draft = 0,
        Submitted = 1,
        PendingReview = 2,
    }

    private static async Task<HttpClient> StartAsync(bool withMiddleware)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        if (withMiddleware)
            app.UseMiddleware<EnumQueryMiddleware>();
        app.MapGet("/items", (Stage? stage, bool? mine, int? page) => Results.Text($"{stage?.ToString() ?? "none"}|{mine?.ToString() ?? "none"}|{page?.ToString(CultureInfo.InvariantCulture) ?? "none"}"));
        app.MapGet("/many", (Stage[]? stages) => Results.Text(string.Join(",", stages ?? [])));
        await app.StartAsync();
        return app.GetTestClient();
    }

    [Theory]
    [InlineData("stage=draft", "Draft|none|none")]
    [InlineData("stage=Draft", "Draft|none|none")]
    [InlineData("stage=DRAFT", "Draft|none|none")]
    [InlineData("stage=submitted&mine=true&page=2", "Submitted|True|2")]
    [InlineData("stage=pendingReview", "PendingReview|none|none")]
    [InlineData("stage=1", "Submitted|none|none")]
    public async Task The_documented_camel_case_value_binds(string query, string expected)
    {
        using var client = await StartAsync(withMiddleware: true);
        var response = await client.GetAsync($"/items?{query}", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(expected);
    }

    [Fact]
    public async Task A_value_that_is_not_a_member_is_still_a_bad_request()
    {
        using var client = await StartAsync(withMiddleware: true);
        var response = await client.GetAsync("/items?stage=archived", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Repeated_values_are_all_matched()
    {
        using var client = await StartAsync(withMiddleware: true);
        var response = await client.GetAsync("/many?stages=draft&stages=submitted", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Draft,Submitted");
    }

    [Fact]
    public async Task Without_the_middleware_ASP_NET_rejects_the_lower_case_value()
    {
        // Documents the behaviour this middleware exists for; if ASP.NET ever changes it, the middleware can go.
        using var client = await StartAsync(withMiddleware: false);
        var response = await client.GetAsync("/items?stage=draft", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
    }
}
