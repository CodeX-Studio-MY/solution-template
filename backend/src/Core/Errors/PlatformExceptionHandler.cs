using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Acme.Core.Errors;

/// <summary>Turns exceptions into RFC 9457 <c>ProblemDetails</c>. Never returns stack traces outside Development.</summary>
internal sealed partial class PlatformExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment environment,
    ILogger<PlatformExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (error, detail, fieldErrors) = exception switch
        {
            ValidationException validation => (validation.Error, validation.Detail, validation.Errors),
            PlatformException platform => (platform.Error, platform.Detail, null),
            DbUpdateConcurrencyException => (PlatformErrors.PreconditionFailed, null, null),
            BadHttpRequestException bad => (PlatformErrors.Validation with { Status = bad.StatusCode }, null, null),
            _ => (PlatformErrors.Unexpected, null, (IReadOnlyDictionary<string, string[]>?)null),
        };

        if (error.Status >= 500)
            LogUnhandled(logger, exception);

        var problem = fieldErrors is null
            ? new ProblemDetails()
            : new ValidationProblemDetails(fieldErrors.ToDictionary(e => e.Key, e => e.Value));
        problem.Status = error.Status;
        problem.Title = error.Code;
        problem.Type = $"urn:problem:{error.Code}";
        problem.Detail = environment.IsDevelopment() ? detail ?? (error.Status >= 500 ? exception.ToString() : null) : null;
        problem.Extensions["code"] = error.Code;
        problem.Extensions["messageKey"] = error.MessageKey;
        if (exception is PlatformException { Arguments.Count: > 0 } withArguments)
            problem.Extensions["arguments"] = withArguments.Arguments;

        httpContext.Response.StatusCode = error.Status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
