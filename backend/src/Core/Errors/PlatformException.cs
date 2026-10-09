namespace Acme.Core.Errors;

/// <summary>A business or platform error that maps to a <c>ProblemDetails</c> response.</summary>
public abstract class PlatformException : Exception
{
    protected PlatformException(ErrorCode error, string? detail = null, Exception? inner = null)
        : base(detail ?? error.Code, inner)
    {
        Error = error;
        Detail = detail;
    }

    public ErrorCode Error { get; }

    /// <summary>A developer-facing detail. User-facing text comes from <see cref="ErrorCode.MessageKey"/>.</summary>
    public string? Detail { get; }

    /// <summary>Values the localized message can interpolate.</summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; init; } = new Dictionary<string, object?>();

    /// <summary>Response headers that belong to the error (for example <c>Retry-After</c> or <c>WWW-Authenticate</c>).</summary>
    public virtual IReadOnlyDictionary<string, string> Headers { get; } = new Dictionary<string, string>();
}

public sealed class NotFoundException(ErrorCode? error = null, string? detail = null)
    : PlatformException(error ?? PlatformErrors.NotFound, detail);

public sealed class ConflictException(ErrorCode? error = null, string? detail = null)
    : PlatformException(error ?? PlatformErrors.Conflict, detail);

public sealed class ForbiddenException(ErrorCode? error = null, string? detail = null)
    : PlatformException(error ?? PlatformErrors.Forbidden, detail);

/// <summary>The request is valid but breaks a business rule (422).</summary>
public sealed class BusinessRuleException(ErrorCode? error = null, string? detail = null)
    : PlatformException(error ?? PlatformErrors.BusinessRule, detail);

/// <summary>The <c>If-Match</c> ETag no longer matches (412).</summary>
public sealed class PreconditionFailedException(string? detail = null)
    : PlatformException(PlatformErrors.PreconditionFailed, detail);

/// <summary>An update arrived without <c>If-Match</c> (428), so a lost update can't happen silently.</summary>
public sealed class PreconditionRequiredException(string? detail = null)
    : PlatformException(PlatformErrors.PreconditionRequired, detail);

/// <summary>Validation failed (400), with errors per field.</summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors, ErrorCode? error = null)
    : PlatformException(error ?? PlatformErrors.Validation)
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
