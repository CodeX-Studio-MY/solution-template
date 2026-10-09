using Acme.Core.Errors;
using Acme.Core.Platform;

namespace Acme.Tenancy.Application;

/// <summary>The Tenancy module's error catalog.</summary>
[ErrorCatalog(Modules.TenantSettings)]
public static class TenancyErrors
{
    public static readonly ErrorCode SlugInvalid = new("tenant-settings.slug-invalid", 400, "errors.tenancy.slugInvalid");
    public static readonly ErrorCode SlugInUse = new("tenant-settings.slug-in-use", 409, "errors.tenancy.slugInUse");
    public static readonly ErrorCode UnknownPlan = new("tenant-settings.unknown-plan", 422, "errors.tenancy.unknownPlan");
    public static readonly ErrorCode InvalidTransition = new("tenant-settings.invalid-transition", 422, "errors.tenancy.invalidTransition");
    public static readonly ErrorCode TenantNotFound = new("tenant-settings.tenant-not-found", 404, "errors.tenancy.tenantNotFound");
    public static readonly ErrorCode OnboardingFailed = new("tenant-settings.onboarding-failed", 500, "errors.tenancy.onboardingFailed");
    public static readonly ErrorCode OperatorLoginFailed = new("tenant-settings.operator-login-failed", 401, "errors.tenancy.operatorLoginFailed");
    public static readonly ErrorCode SupportAccessNotUsable = new("tenant-settings.support-access-not-usable", 403, "errors.tenancy.supportAccessNotUsable");
    public static readonly ErrorCode SupportAccessInvalid = new("tenant-settings.support-access-invalid", 422, "errors.tenancy.supportAccessInvalid");
    public static readonly ErrorCode GrantNotFound = new("tenant-settings.grant-not-found", 404, "errors.tenancy.grantNotFound");
    public static readonly ErrorCode OperatorExists = new("tenant-settings.operator-exists", 409, "errors.tenancy.operatorExists");
    public static readonly ErrorCode UnknownSettings = new("tenant-settings.unknown-settings", 404, "errors.tenancy.unknownSettings");
    public static readonly ErrorCode UnknownFeature = new("tenant-settings.unknown-feature", 404, "errors.tenancy.unknownFeature");
    public static readonly ErrorCode ReservedSlug = new("tenant-settings.reserved-slug", 400, "errors.tenancy.reservedSlug");
}
