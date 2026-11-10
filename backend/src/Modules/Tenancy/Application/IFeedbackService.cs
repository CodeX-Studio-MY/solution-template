using Saaskit.Core.Errors;
using Saaskit.Core.Http;
using Saaskit.Core.Platform;
using Saaskit.Tenancy.Domain;

namespace Saaskit.Tenancy.Application;

/// <summary>The Feedback module's error catalog. Message and range problems are ordinary validation errors (400).</summary>
[ErrorCatalog(Modules.Feedback)]
public static class FeedbackErrors
{
    public static readonly ErrorCode NotFound = new("feedback.not-found", 404, "errors.feedback.notFound");
}

/// <summary>What a user sends. <see cref="Rating"/> is optional (1 to 5); <see cref="PagePath"/> is the page they were on.</summary>
public sealed record FeedbackSubmit(FeedbackKind Kind, byte? Rating, string Message, string? PagePath);

/// <summary>One piece of feedback with its tenant, as both the tenant's admins and the operator list it.</summary>
public sealed record FeedbackRow(Guid FeedbackId, Guid TenantId, string TenantSlug, string TenantName, Guid SenderId, string SenderName, FeedbackKind Kind, byte? Rating, string Message, string PagePath, FeedbackStatus Status, DateTime CreatedOn, string ETag);

/// <summary>Feedback from a tenant's users to the operator. A tenant's admins read their own; the operator reads every tenant's and triages it.</summary>
public interface IFeedbackService
{
    Task<FeedbackRow> SubmitAsync(Guid tenantId, Guid senderId, FeedbackSubmit input, CancellationToken cancellationToken = default);

    Task<Page<FeedbackRow>> ListForTenantAsync(Guid tenantId, PageRequest page, CancellationToken cancellationToken = default);

    Task<Page<FeedbackRow>> ListAllAsync(FeedbackStatus? status, Guid? tenantId, PageRequest page, CancellationToken cancellationToken = default);

    Task<FeedbackRow> SetStatusAsync(Guid feedbackId, FeedbackStatus status, string? ifMatch, Guid actor, CancellationToken cancellationToken = default);
}
