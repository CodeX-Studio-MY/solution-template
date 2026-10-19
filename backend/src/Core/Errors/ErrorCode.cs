namespace Saaskit.Core.Errors;

/// <summary>
/// One entry in a module's error catalog: a stable code, its HTTP status and the i18n key of its message.
/// Declared once per module (see <see cref="PlatformErrors"/>); the frontend's error-code type is generated from the catalogs.
/// </summary>
/// <param name="Code">Stable code, <c>{module-key}.{error}</c>, returned in <c>ProblemDetails</c> as <c>code</c>.</param>
/// <param name="Status">The HTTP status.</param>
/// <param name="MessageKey">The i18n key of the user-facing message.</param>
public sealed record ErrorCode(string Code, int Status, string MessageKey);

/// <summary>Marks a static class as a module's error catalog; every <see cref="ErrorCode"/> field in it is published.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ErrorCatalogAttribute(string moduleKey) : Attribute
{
    public string ModuleKey { get; } = moduleKey;
}

/// <summary>Errors raised by the platform itself.</summary>
[ErrorCatalog("platform")]
public static class PlatformErrors
{
    public static readonly ErrorCode Validation = new("platform.validation", 400, "errors.platform.validation");
    public static readonly ErrorCode Unauthenticated = new("platform.unauthenticated", 401, "errors.platform.unauthenticated");
    public static readonly ErrorCode Forbidden = new("platform.forbidden", 403, "errors.platform.forbidden");
    public static readonly ErrorCode TenantMismatch = new("platform.tenant-mismatch", 403, "errors.platform.tenantMismatch");
    public static readonly ErrorCode TenantHeaderNotAllowed = new("platform.tenant-header-not-allowed", 403, "errors.platform.tenantHeaderNotAllowed");
    public static readonly ErrorCode TenantReadOnly = new("platform.tenant-read-only", 403, "errors.platform.tenantReadOnly");
    public static readonly ErrorCode TenantUnavailable = new("platform.tenant-unavailable", 403, "errors.platform.tenantUnavailable");
    public static readonly ErrorCode ModuleNotEntitled = new("platform.module-not-entitled", 403, "errors.platform.moduleNotEntitled");
    public static readonly ErrorCode PrincipalTypeNotAllowed = new("platform.principal-type-not-allowed", 403, "errors.platform.principalTypeNotAllowed");
    public static readonly ErrorCode AccessChanged = new("platform.access-changed", 401, "errors.platform.accessChanged");
    public static readonly ErrorCode AccessCheckUnavailable = new("platform.access-check-unavailable", 503, "errors.platform.accessCheckUnavailable");
    public static readonly ErrorCode RateLimited = new("platform.rate-limited", 429, "errors.platform.rateLimited");
    public static readonly ErrorCode ServiceTokenUnavailable = new("platform.service-token-unavailable", 403, "errors.platform.serviceTokenUnavailable");
    public static readonly ErrorCode TenantNotFound = new("platform.tenant-not-found", 404, "errors.platform.tenantNotFound");
    public static readonly ErrorCode NotFound = new("platform.not-found", 404, "errors.platform.notFound");
    public static readonly ErrorCode Conflict = new("platform.conflict", 409, "errors.platform.conflict");
    public static readonly ErrorCode PreconditionFailed = new("platform.precondition-failed", 412, "errors.platform.preconditionFailed");
    public static readonly ErrorCode BusinessRule = new("platform.business-rule", 422, "errors.platform.businessRule");
    public static readonly ErrorCode PreconditionRequired = new("platform.precondition-required", 428, "errors.platform.preconditionRequired");
    public static readonly ErrorCode IdempotencyKeyReused = new("platform.idempotency-key-reused", 422, "errors.platform.idempotencyKeyReused");
    public static readonly ErrorCode IdempotencyInProgress = new("platform.idempotency-in-progress", 409, "errors.platform.idempotencyInProgress");
    public static readonly ErrorCode Unexpected = new("platform.unexpected", 500, "errors.platform.unexpected");
}
