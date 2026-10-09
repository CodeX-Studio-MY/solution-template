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

/// <summary>One problem with one field: a message key (from the localization catalog) and the values it interpolates.</summary>
public sealed record FieldError(string Field, string Key, IReadOnlyList<object?> Arguments)
{
    public FieldError(string field, string key, params object?[] arguments)
        : this(field, key, (IReadOnlyList<object?>)arguments)
    {
    }
}

/// <summary>Validation failed (400), with errors per field. Messages are keys, localized when the response is written.</summary>
public sealed class ValidationException : PlatformException
{
    public ValidationException(IEnumerable<FieldError> errors, ErrorCode? error = null)
        : base(error ?? PlatformErrors.Validation)
    {
        Errors = [.. errors];
    }

    /// <summary>Plain text or message keys per field.</summary>
    public ValidationException(IReadOnlyDictionary<string, string[]> errors, ErrorCode? error = null)
        : this(errors.SelectMany(e => e.Value.Select(message => new FieldError(e.Key, message, (IReadOnlyList<object?>)[]))), error)
    {
    }

    public IReadOnlyList<FieldError> Errors { get; }
}

/// <summary>Collects field errors, then throws one <see cref="ValidationException"/> if there are any.</summary>
public sealed class ValidationBuilder
{
    private readonly List<FieldError> _errors = [];

    public bool HasErrors => _errors.Count > 0;

    public ValidationBuilder Add(string field, string key, params object?[] arguments)
    {
        _errors.Add(new FieldError(field, key, arguments));
        return this;
    }

    public ValidationBuilder Required(string field, string? value, string label) =>
        string.IsNullOrWhiteSpace(value) ? Add(field, "validation.required", label) : this;

    public ValidationBuilder MaxLength(string field, string? value, int max, string label) =>
        value is not null && value.Length > max ? Add(field, "validation.maxLength", label, max) : this;

    public void ThrowIfAny()
    {
        if (_errors.Count > 0)
            throw new ValidationException(_errors);
    }
}
