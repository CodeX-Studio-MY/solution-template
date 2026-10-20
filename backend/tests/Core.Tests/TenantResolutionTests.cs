using System.Security.Claims;
using Farm360.Core.Configuration;
using Farm360.Core.Errors;
using Farm360.Core.Security;
using Farm360.Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Farm360.Core.Tests;

public sealed class TenantResolutionTests
{
    private readonly TenantInfo _demo = TestTenants.Create("demo");
    private readonly TenantInfo _other = TestTenants.Create("demo-plus");
    private readonly ITenantDirectory _directory = Substitute.For<ITenantDirectory>();

    public TenantResolutionTests()
    {
        _directory.FindByIdAsync(_demo.Id, Arg.Any<CancellationToken>()).Returns(_demo);
        _directory.FindBySlugAsync("demo", Arg.Any<CancellationToken>()).Returns(_demo);
        _directory.FindByIdAsync(_other.Id, Arg.Any<CancellationToken>()).Returns(_other);
        _directory.FindBySlugAsync("demo-plus", Arg.Any<CancellationToken>()).Returns(_other);
    }

    [Theory]
    [InlineData("demo.localhost", "localhost", "demo")]
    [InlineData("DEMO.localhost", "localhost", "demo")]
    [InlineData("demo.example.com", "example.com", "demo")]
    [InlineData("localhost", "localhost", null)]
    [InlineData("login.example.com", "example.com", null)]
    [InlineData("a.b.example.com", "example.com", null)]
    [InlineData("demo.other.com", "example.com", null)]
    public void Subdomain_is_the_tenant_slug(string host, string baseDomain, string? expected) =>
        TenantResolutionMiddleware.SubdomainOf(host, baseDomain).ShouldBe(expected);

    [Fact]
    public async Task Authenticated_requests_use_the_token_tenant()
    {
        var resolved = await ResolveAsync("demo.localhost", User(_demo.Id));
        resolved.ShouldBe(_demo);
    }

    [Fact]
    public async Task A_subdomain_naming_another_tenant_is_forbidden()
    {
        var ex = await Should.ThrowAsync<ForbiddenException>(() => ResolveAsync("demo-plus.localhost", User(_demo.Id)));
        ex.Error.ShouldBe(PlatformErrors.TenantMismatch);
    }

    [Fact]
    public async Task X_Tenant_with_a_tenant_token_is_forbidden()
    {
        var ex = await Should.ThrowAsync<ForbiddenException>(() => ResolveAsync("localhost", User(_demo.Id), xTenant: "demo-plus"));
        ex.Error.ShouldBe(PlatformErrors.TenantHeaderNotAllowed);
    }

    [Fact]
    public async Task Platform_service_principals_name_the_tenant_with_X_Tenant()
    {
        var resolved = await ResolveAsync("localhost", Principal(PrincipalType.Service, tenantId: null, PlatformPermissions.TenantAny), xTenant: "demo-plus");
        resolved.ShouldBe(_other);
    }

    [Fact]
    public async Task X_Tenant_from_a_service_without_tenant_any_is_forbidden()
    {
        var ex = await Should.ThrowAsync<ForbiddenException>(() =>
            ResolveAsync("localhost", Principal(PrincipalType.Service, tenantId: null), xTenant: "demo"));
        ex.Error.ShouldBe(PlatformErrors.TenantHeaderNotAllowed);
    }

    [Fact]
    public async Task X_Tenant_from_a_user_without_a_tenant_is_forbidden() =>
        await Should.ThrowAsync<ForbiddenException>(() =>
            ResolveAsync("localhost", Principal(PrincipalType.User, tenantId: null, PlatformPermissions.TenantAny), xTenant: "demo"));

    [Fact]
    public async Task Anonymous_requests_use_the_subdomain()
    {
        var resolved = await ResolveAsync("demo-plus.localhost", new ClaimsPrincipal(new ClaimsIdentity()));
        resolved.ShouldBe(_other);
    }

    [Fact]
    public async Task Anonymous_X_Tenant_is_forbidden() =>
        await Should.ThrowAsync<ForbiddenException>(() =>
            ResolveAsync("localhost", new ClaimsPrincipal(new ClaimsIdentity()), xTenant: "demo"));

    [Fact]
    public async Task An_unknown_subdomain_is_not_found() =>
        await Should.ThrowAsync<NotFoundException>(() =>
            ResolveAsync("nobody.localhost", new ClaimsPrincipal(new ClaimsIdentity())));

    private async Task<TenantInfo?> ResolveAsync(string host, ClaimsPrincipal user, string? xTenant = null)
    {
        var context = new DefaultHttpContext { User = user };
        context.Request.Host = new HostString(host);
        if (xTenant is not null)
            context.Request.Headers[PlatformHeaders.Tenant] = xTenant;

        TenantInfo? seen = null;
        var middleware = new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            Options.Create(new TenancyOptions { BaseDomain = "localhost" }),
            NullLogger<TenantResolutionMiddleware>.Instance);
        var tenantContext = new TenantContext();
        await middleware.InvokeAsync(context, _directory, tenantContext);
        if (tenantContext.HasTenant)
            seen = tenantContext.Tenant;
        return seen;
    }

    private static ClaimsPrincipal User(Guid tenantId) => Principal(PrincipalType.User, tenantId);

    private static ClaimsPrincipal Principal(PrincipalType type, Guid? tenantId, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(PlatformClaims.Subject, Guid.NewGuid().ToString()),
            new(PlatformClaims.PrincipalType, type.ToString().ToLowerInvariant()),
        };
        if (tenantId is not null)
            claims.Add(new Claim(PlatformClaims.TenantId, tenantId.Value.ToString()));
        claims.AddRange(permissions.Select(p => new Claim(PlatformClaims.Permissions, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
