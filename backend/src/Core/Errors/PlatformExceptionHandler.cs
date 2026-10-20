using Farm360.Core.Localization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Farm360.Core.Errors;

/// <summary>Turns exceptions into RFC 9457 <c>ProblemDetails</c>. Never returns stack traces outside Development.</summary>
internal sealed partial class PlatformExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment environment,
    ILogger<PlatformExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var localizer = httpContext.RequestServices.GetRequiredService<IMessageLocalizer>();
        (ErrorCode error, string? detail, IReadOnlyDictionary<string, string[]>? fieldErrors) = exception switch
        {
            ValidationException validation => (validation.Error, validation.Detail, ToFieldMessages(validation, localizer)),
            PlatformException platform => (platform.Error, platform.Detail, null),
            DbUpdateConcurrencyException => (PlatformErrors.PreconditionFailed, null, null),
            BadHttpRequestException bad => (PlatformErrors.Validation with { Status = bad.StatusCode }, null, null),
            _ => (PlatformErrors.Unexpected, null, null),
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
        problem.Extensions["message"] = localizer.Text(error.MessageKey);   // in the request's language
        if (exception is PlatformException { Arguments.Count: > 0 } withArguments)
            problem.Extensions["arguments"] = withArguments.Arguments;

        httpContext.Response.StatusCode = error.Status;
        if (exception is PlatformException { Headers.Count: > 0 } withHeaders)
        {
            foreach (var (name, value) in withHeaders.Headers)
                httpContext.Response.Headers[name] = value;
        }
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    /// <summary>Each field's messages in the request's language, with the field name interpolated where the text asks for it.</summary>
    private static Dictionary<string, string[]> ToFieldMessages(ValidationException validation, IMessageLocalizer localizer) =>
        validation.Errors
            .GroupBy(e => e.Field, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => localizer.Text(e.Key, [.. e.Arguments])).ToArray(), StringComparer.Ordinal);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
