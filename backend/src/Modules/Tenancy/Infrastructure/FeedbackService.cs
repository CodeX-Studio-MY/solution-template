using System.Text.Json;
using Saaskit.Core.Catalog;
using Saaskit.Core.Data;
using Saaskit.Core.Errors;
using Saaskit.Core.Http;
using Saaskit.Tenancy.Application;
using Saaskit.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Saaskit.Tenancy.Infrastructure;

internal sealed partial class FeedbackService(
    TenancyCatalogDbContext db,
    CatalogDbContext catalog,
    IIdentityClient identity,
    TimeProvider time,
    ILogger<FeedbackService> logger) : IFeedbackService
{
    public async Task<FeedbackRow> SubmitAsync(Guid tenantId, Guid senderId, FeedbackSubmit input, CancellationToken cancellationToken = default)
    {
        var message = input.Message?.Trim() ?? "";
        var pagePath = input.PagePath?.Trim() ?? "";
        var errors = new List<FieldError>();
        if (!Enum.IsDefined(input.Kind))
            errors.Add(new FieldError("kind", "validation.unknownOption", ["kind"]));
        if (input.Rating is < 1 or > 5)
            errors.Add(new FieldError("rating", "validation.range", ["rating", 1, 5]));
        if (message.Length == 0)
            errors.Add(new FieldError("message", "validation.required", ["message"]));
        else if (message.Length > Feedback.MaxMessageLength)
            errors.Add(new FieldError("message", "validation.maxLength", ["message", Feedback.MaxMessageLength]));
        // The column is ASCII (a path is percent-encoded), so anything else is not a path this app sent.
        if (pagePath.Length > Feedback.MaxPagePathLength || pagePath.Any(c => c is < ' ' or > '~'))
            errors.Add(new FieldError("pagePath", "validation.invalid", ["pagePath"]));
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var tenant = await catalog.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new NotFoundException(TenancyErrors.TenantNotFound);
        // A snapshot: the operator can't read the tenant's database. The token carries only the ID, so ask Identity for the name.
        var name = await NameOfAsync(tenantId, senderId, cancellationToken) ?? senderId.ToString();
        var feedback = new Feedback(tenantId, senderId, name.Length > Feedback.MaxSenderNameLength ? name[..Feedback.MaxSenderNameLength] : name,
            input.Kind, input.Rating, message, pagePath);
        db.FeedbackEntries.Add(feedback);
        await db.SaveChangesAsync(cancellationToken);
        return ToRow(feedback, tenant.Slug, tenant.DisplayName);
    }

    public Task<Page<FeedbackRow>> ListForTenantAsync(Guid tenantId, PageRequest page, CancellationToken cancellationToken = default) =>
        PageAsync(db.FeedbackEntries.Where(f => f.TenantId == tenantId), page, cancellationToken);

    public Task<Page<FeedbackRow>> ListAllAsync(FeedbackStatus? status, Guid? tenantId, PageRequest page, CancellationToken cancellationToken = default)
    {
        IQueryable<Feedback> query = db.FeedbackEntries;
        if (status is not null)
            query = query.Where(f => f.Status == status);
        if (tenantId is not null)
            query = query.Where(f => f.TenantId == tenantId);
        return PageAsync(query, page, cancellationToken);
    }

    public async Task<FeedbackRow> SetStatusAsync(Guid feedbackId, FeedbackStatus status, string? ifMatch, Guid actor, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(status))
            throw new ValidationException([new FieldError("status", "validation.unknownOption", ["status"])]);
        var feedback = await db.FeedbackEntries.SingleOrDefaultAsync(f => f.Id == feedbackId, cancellationToken)
            ?? throw new NotFoundException(FeedbackErrors.NotFound);
        db.ApplyIfMatch(feedback, ifMatch);
        var from = feedback.Status;
        feedback.SetStatus(status);
        db.AuditLog.Add(new TenancyAuditEntry(time.GetUtcNow().UtcDateTime, feedback.TenantId, "feedback-status-changed", actor.ToString(),
            JsonSerializer.Serialize(new { feedbackId, from = from.ToString(), to = status.ToString() })));
        await db.SaveChangesAsync(cancellationToken);
        var tenant = await catalog.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == feedback.TenantId, cancellationToken);
        return ToRow(feedback, tenant?.Slug ?? "", tenant?.DisplayName ?? "");
    }

    /// <summary>Newest first. Tracked, because the ETag is read from the row version.</summary>
    private async Task<Page<FeedbackRow>> PageAsync(IQueryable<Feedback> query, PageRequest page, CancellationToken cancellationToken)
    {
        var found = await query.OrderByDescending(f => f.CreatedOn).ThenBy(f => f.Id).ToPageAsync(page, cancellationToken);
        var ids = found.Items.Select(f => f.TenantId).Distinct().ToList();
        var tenants = await catalog.Tenants.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
        return found.Map(f => tenants.TryGetValue(f.TenantId, out var t) ? ToRow(f, t.Slug, t.DisplayName) : ToRow(f, "", ""));
    }

    private FeedbackRow ToRow(Feedback f, string slug, string tenantName) =>
        new(f.Id, f.TenantId, slug, tenantName, f.SenderId, f.SenderName, f.Kind, f.Rating, f.Message, f.PagePath, f.Status, f.CreatedOn, db.GetETag(f));

    private async Task<string?> NameOfAsync(Guid tenantId, Guid principalId, CancellationToken cancellationToken)
    {
        try
        {
            return await identity.GetPrincipalNameAsync(tenantId, principalId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNameFailed(logger, ex, tenantId);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not look up the name of a feedback sender in tenant {TenantId}")]
    private static partial void LogNameFailed(ILogger logger, Exception exception, Guid tenantId);
}
