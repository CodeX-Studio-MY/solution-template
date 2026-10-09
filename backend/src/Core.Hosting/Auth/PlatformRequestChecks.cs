using System.Threading.RateLimiting;
using Acme.Core.Configuration;
using Acme.Core.Errors;
using Acme.Core.Hosting.Redis;
using Acme.Core.Security;
using Acme.Core.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Acme.Core.Hosting.Auth;

/// <summary>Rejects tokens whose <c>access_version</c> is stale (401). Runs after authentication and tenant resolution.</summary>
internal sealed class AccessVersionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenant, AccessVersionChecker checker)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true && tenant.HasTenant && user.FindFirst(PlatformClaims.Subject)?.Value is { } subject)
        {
            var current = await checker.IsCurrentAsync(tenant.Tenant.Id, subject, user.FindFirst(PlatformClaims.AccessVersion)?.Value, context.RequestAborted);
            if (!current)
                throw new AccessChangedException();
        }
        await next(context);
    }
}

public sealed class AccessChangedException() : PlatformException(PlatformErrors.AccessChanged, "The principal's access changed; refresh the token.")
{
    public override IReadOnlyDictionary<string, string> Headers => new Dictionary<string, string>
    {
        [HeaderNames.WWWAuthenticate] = "Bearer error=\"invalid_token\", error_description=\"access changed\"",
    };
}

/// <summary>
/// Per-tenant and per-principal limits shared across instances (Redis), from the tenant's plan, plus a coarse
/// in-memory per-instance limit. Exceeding a limit returns 429 with Retry-After.
/// </summary>
internal sealed partial class RateLimitMiddleware(
    RequestDelegate next,
    IOptions<RateLimitOptions> options,
    ILogger<RateLimitMiddleware> logger) : IDisposable
{
    private readonly FixedWindowRateLimiter _instance = new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = options.Value.PerInstancePerSecond,
        Window = TimeSpan.FromSeconds(1),
        QueueLimit = 0,
    });

    public async Task InvokeAsync(HttpContext context, ITenantContext tenant, RedisRateLimiter limiter)
    {
        using var lease = _instance.AttemptAcquire();
        if (!lease.IsAcquired)
        {
            Reject(context, 1);
            return;
        }

        if (context.GetEndpoint()?.Metadata.GetMetadata<AuthRateLimited>() is not null)
        {
            var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            try
            {
                var wait = await limiter.TryAcquireAsync($"auth:{address}", options.Value.AuthPerAddressPerMinute);
                if (wait is not null)
                {
                    Reject(context, wait.Value);
                    return;
                }
            }
            catch (StackExchange.Redis.RedisException ex)
            {
                LogLimiterUnavailable(logger, ex);
            }
        }

        if (tenant.HasTenant)
        {
            var info = tenant.Tenant;
            var perTenant = info.Limits?.PerTenantPerMinute ?? options.Value.PerTenantPerMinute;
            var perPrincipal = info.Limits?.PerPrincipalPerMinute ?? options.Value.PerPrincipalPerMinute;
            try
            {
                var retryAfter = await limiter.TryAcquireAsync($"{info.Id}:tenant", perTenant);
                if (retryAfter is null && context.User.FindFirst(PlatformClaims.Subject)?.Value is { } subject)
                    retryAfter = await limiter.TryAcquireAsync($"{info.Id}:principal:{subject}", perPrincipal);
                if (retryAfter is not null)
                {
                    Reject(context, retryAfter.Value);
                    return;
                }
            }
            catch (StackExchange.Redis.RedisException ex)
            {
                // Rate limits fail open: an unavailable limiter must not take the service down.
                LogLimiterUnavailable(logger, ex);
            }
        }
        await next(context);
    }

    public void Dispose() => _instance.Dispose();

    private static void Reject(HttpContext context, int retryAfterSeconds) =>
        throw new RateLimitedException(retryAfterSeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Distributed rate limiter unavailable; request allowed")]
    private static partial void LogLimiterUnavailable(ILogger logger, Exception exception);
}

public sealed class RateLimitedException(int retryAfterSeconds) : PlatformException(PlatformErrors.RateLimited)
{
    public override IReadOnlyDictionary<string, string> Headers => new Dictionary<string, string>
    {
        [HeaderNames.RetryAfter] = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

/// <summary>Turns authorization failures into <c>ProblemDetails</c> with the failure's error code.</summary>
internal sealed class PlatformAuthorizationResultHandler(IProblemDetailsService problems) : Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler
{
    private readonly Microsoft.AspNetCore.Authorization.Policy.AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        Microsoft.AspNetCore.Authorization.AuthorizationPolicy policy,
        Microsoft.AspNetCore.Authorization.Policy.PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var code = authorizeResult.AuthorizationFailure?.FailureReasons.FirstOrDefault()?.Message ?? PlatformErrors.Forbidden.Code;
        var error = typeof(PlatformErrors).GetFields()
            .Select(f => f.GetValue(null)).OfType<ErrorCode>()
            .FirstOrDefault(e => e.Code == code) ?? PlatformErrors.Forbidden;
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        var problem = new ProblemDetails { Status = 403, Title = error.Code, Type = $"urn:problem:{error.Code}" };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["messageKey"] = error.MessageKey;
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
