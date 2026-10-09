using System.Globalization;
using System.Text.RegularExpressions;
using Acme.Contracts;
using Acme.Core.Authorization;
using Acme.Core.Errors;
using Acme.Core.Http;
using Acme.Core.Platform;
using Acme.Core.Security;
using Acme.Core.Tenancy;
using Acme.Core.Time;
using Acme.PurchaseRequest.Domain;

namespace Acme.PurchaseRequest.Application;

/// <summary>The purchase-request use cases. Every business rule is in the domain or here; endpoints only translate HTTP.</summary>
public sealed class RequestService(
    IRequestRepository requests,
    IRequestSettings settings,
    IApprovalPolicyResolver policies,
    IQuoteFiles quotes,
    IAccessEvaluator access,
    ICurrentPrincipal principal,
    ITenantClock clock,
    ITenantContext tenant,
    TimeProvider time)
{
    // ---- Reads ----------------------------------------------------------------------------------

    public async Task<RequestDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var request = await FindVisibleAsync(id, cancellationToken);
        return await ToDtoAsync(request, cancellationToken);
    }

    /// <summary>Users see their own requests; managers and admins see everyone's.</summary>
    public async Task<Page<RequestSummaryDto>> ListAsync(PageRequest page, RequestStatus? status, bool mineOnly, CancellationToken cancellationToken = default)
    {
        var onlyMine = mineOnly || !access.HasRole(Modules.PurchaseRequest, Role.Manager);
        var result = await requests.ListAsync(page, status, onlyMine ? CurrentUserId : null, cancellationToken);
        return result.Map(r => new RequestSummaryDto(r.Id, r.Number, r.Title, r.Status, r.TotalAmount, r.Currency, r.BusinessDate, r.RequesterId));
    }

    // ---- Changes --------------------------------------------------------------------------------

    public async Task<RequestDto> CreateAsync(SaveRequestInput input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        var businessDate = clock.Today;
        var config = await settings.GetAsync(cancellationToken);
        var number = await NextNumberAsync(config.NumberFormat, businessDate, cancellationToken);

        var request = new Domain.PurchaseRequest(number, CurrentUserId, input.Title, input.Description, businessDate, tenant.Tenant.Currency);
        request.ReplaceDetails(input.Title, input.Description, input.Lines.Select(l => new RequestLine(l.Item, l.Quantity, l.UnitPrice)));
        requests.Add(request);
        await requests.SaveAsync(cancellationToken);
        return await ToDtoAsync(request, cancellationToken);
    }

    public async Task<RequestDto> UpdateAsync(Guid id, string? ifMatch, SaveRequestInput input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        var request = await FindEditableByOwnerAsync(id, ifMatch, cancellationToken);
        Apply(() => request.ReplaceDetails(input.Title, input.Description, input.Lines.Select(l => new RequestLine(l.Item, l.Quantity, l.UnitPrice))));
        await requests.SaveAsync(cancellationToken);
        return await ToDtoAsync(request, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, string? ifMatch, CancellationToken cancellationToken = default)
    {
        var request = await FindEditableByOwnerAsync(id, ifMatch, cancellationToken);
        Apply(() => request.MarkDeleted(time.GetUtcNow().UtcDateTime));
        await requests.SaveAsync(cancellationToken);
    }

    public async Task<RequestDto> SubmitAsync(Guid id, string? ifMatch, CancellationToken cancellationToken = default)
    {
        var request = await FindEditableByOwnerAsync(id, ifMatch, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        Apply(() => request.Submit(now));
        requests.Publish(new PurchaseRequestSubmitted.V1(request.Id, request.Number, request.RequesterId, request.TotalAmount, request.Currency, request.BusinessDate));

        // Small requests don't need a person: below the tenant's threshold they are approved on submit.
        var config = await settings.GetAsync(cancellationToken);
        if (request.TotalAmount < config.AutoApproveBelow)
        {
            Apply(() => request.AutoApprove(now));
            requests.Publish(new PurchaseRequestApproved.V1(request.Id, request.Number, request.RequesterId, request.RequesterId, request.TotalAmount, request.Currency, request.BusinessDate, AutoApproved: true));
        }
        await requests.SaveAsync(cancellationToken);
        return await ToDtoAsync(request, cancellationToken);
    }

    public async Task<RequestDto> ApproveAsync(Guid id, string? ifMatch, CancellationToken cancellationToken = default)
    {
        var request = await FindVisibleAsync(id, cancellationToken);
        requests.ApplyIfMatch(request, ifMatch);

        var policy = await policies.ResolveAsync(cancellationToken);
        var step = policy.NextStep(request) ?? throw new BusinessRuleException(PurchaseRequestErrors.NotSubmitted);
        if (!access.HasRole(Modules.PurchaseRequest, step.MinimumRole))
            throw new ForbiddenException(PurchaseRequestErrors.RoleTooLow);

        var now = time.GetUtcNow().UtcDateTime;
        Apply(() => request.Approve(step.Level, CurrentUserId, now, policy.NextStep));
        if (request.Status == RequestStatus.Approved)
            requests.Publish(new PurchaseRequestApproved.V1(request.Id, request.Number, request.RequesterId, CurrentUserId, request.TotalAmount, request.Currency, request.BusinessDate, AutoApproved: false));
        await requests.SaveAsync(cancellationToken);
        return await ToDtoAsync(request, cancellationToken);
    }

    public async Task<RequestDto> RejectAsync(Guid id, string? ifMatch, RejectInput input, CancellationToken cancellationToken = default)
    {
        new ValidationBuilder().Required("reason", input.Reason, "Reason").MaxLength("reason", input.Reason, 500, "Reason").ThrowIfAny();
        var request = await FindVisibleAsync(id, cancellationToken);
        requests.ApplyIfMatch(request, ifMatch);

        Apply(() => request.Reject(CurrentUserId, input.Reason, time.GetUtcNow().UtcDateTime));
        requests.Publish(new PurchaseRequestRejected.V1(request.Id, request.Number, request.RequesterId, CurrentUserId, request.RejectionReason!));
        await requests.SaveAsync(cancellationToken);
        return await ToDtoAsync(request, cancellationToken);
    }

    /// <summary>A machine at the loading dock confirms the goods arrived. The audit trail records the machine (and the user, if one is signed in on it).</summary>
    public async Task<RequestDto> ReceiveGoodsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var request = await requests.FindAsync(id, cancellationToken) ?? throw new NotFoundException(PurchaseRequestErrors.NotFound);
        Apply(() => request.ReceiveGoods(time.GetUtcNow().UtcDateTime));
        await requests.SaveAsync(cancellationToken);
        return await ToDtoAsync(request, cancellationToken);
    }

    public async Task<RequestDto> AttachQuoteAsync(Guid id, string? ifMatch, Stream content, string claimedName, CancellationToken cancellationToken = default)
    {
        var request = await FindEditableByOwnerAsync(id, ifMatch, cancellationToken);
        var fileId = await quotes.UploadAsync(request.Id, content, claimedName, cancellationToken);
        Apply(() => request.AttachQuote(fileId));
        await requests.SaveAsync(cancellationToken);
        return await ToDtoAsync(request, cancellationToken);
    }

    public async Task<Uri> QuoteDownloadUrlAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var request = await FindVisibleAsync(id, cancellationToken);
        return await quotes.DownloadUrlAsync(request.QuoteFileId ?? throw new NotFoundException(PurchaseRequestErrors.NoQuote), cancellationToken);
    }

    // ---- Rules ----------------------------------------------------------------------------------

    /// <summary>
    /// The actions this caller may take now. Computed from the caller's role, the request's state and the tenant's approval policy,
    /// so the frontend shows buttons from this list and never re-implements the rules.
    /// </summary>
    public async Task<IReadOnlyList<string>> AllowedActionsAsync(Domain.PurchaseRequest request, CancellationToken cancellationToken = default)
    {
        var actions = new List<string>();
        var isOwner = request.RequesterId == CurrentUserId;
        var canAct = access.HasRole(Modules.PurchaseRequest, Role.User);
        if (request.Status == RequestStatus.Draft && isOwner && canAct)
            actions.AddRange([RequestActions.Edit, RequestActions.Delete, RequestActions.Submit, RequestActions.UploadQuote]);

        if (request.Status == RequestStatus.Submitted && !isOwner && access.Can(PurchaseRequestPermissions.Approve))
        {
            var policy = await policies.ResolveAsync(cancellationToken);
            var step = policy.NextStep(request);
            var alreadyApproved = request.Approvals.Any(a => a.ApproverId == CurrentUserId);
            if (step is not null && !alreadyApproved && access.HasRole(Modules.PurchaseRequest, step.MinimumRole))
                actions.Add(RequestActions.Approve);
            // Anyone who may approve may also reject, whatever step the request is at.
            actions.Add(RequestActions.Reject);
        }
        return actions;
    }

    private Guid CurrentUserId =>
        Guid.TryParse(principal.Id, out var id) ? id : throw new ForbiddenException(PlatformErrors.Unauthenticated);

    private async Task<Domain.PurchaseRequest> FindVisibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await requests.FindAsync(id, cancellationToken) ?? throw new NotFoundException(PurchaseRequestErrors.NotFound);
        // Users can only see their own requests; they get "not found", not "forbidden", so ids don't leak.
        if (request.RequesterId != CurrentUserId && !access.HasRole(Modules.PurchaseRequest, Role.Manager))
            throw new NotFoundException(PurchaseRequestErrors.NotFound);
        return request;
    }

    private async Task<Domain.PurchaseRequest> FindEditableByOwnerAsync(Guid id, string? ifMatch, CancellationToken cancellationToken)
    {
        var request = await FindVisibleAsync(id, cancellationToken);
        if (request.RequesterId != CurrentUserId && !access.HasRole(Modules.PurchaseRequest, Role.Admin))
            throw new ForbiddenException(PurchaseRequestErrors.NotYours);
        requests.ApplyIfMatch(request, ifMatch);
        return request;
    }

    private async Task<string> NextNumberAsync(string format, DateOnly businessDate, CancellationToken cancellationToken)
    {
        // The counter is per day ("PR-20261009"), so numbers restart each business day of the tenant.
        var counter = await requests.NextSequenceAsync(NumberFormatter.CounterName(format, businessDate), cancellationToken);
        return NumberFormatter.Format(format, businessDate, counter);
    }

    private async Task<RequestDto> ToDtoAsync(Domain.PurchaseRequest r, CancellationToken cancellationToken) => new(
        r.Id, r.Number, r.RequesterId, r.Title, r.Description, r.Status, r.BusinessDate, r.Currency, r.TotalAmount,
        [.. r.Lines.Select(l => new LineDto(l.Item, l.Quantity, l.UnitPrice, l.LineTotal))],
        [.. r.Approvals.OrderBy(a => a.ApprovedOn).Select(a => new ApprovalDto(a.Level, a.ApproverId, a.ApprovedOn))],
        r.QuoteFileId, r.CreatedOn, r.SubmittedOn, r.DecidedOn, r.AutoApproved, r.RejectionReason, r.GoodsReceivedOn,
        await AllowedActionsAsync(r, cancellationToken),
        requests.ETagOf(r));

    /// <summary>Turns the domain's rule violations into this module's error codes.</summary>
    private static void Apply(Action change)
    {
        try
        {
            change();
        }
        catch (RequestStateException ex)
        {
            throw ex.Error switch
            {
                RequestStateError.NotDraft => new BusinessRuleException(PurchaseRequestErrors.NotDraft),
                RequestStateError.NotSubmitted => new BusinessRuleException(PurchaseRequestErrors.NotSubmitted),
                RequestStateError.NotApproved => new BusinessRuleException(PurchaseRequestErrors.NotApproved),
                RequestStateError.NoLines => new BusinessRuleException(PurchaseRequestErrors.NoLines),
                RequestStateError.OwnRequest => new ForbiddenException(PurchaseRequestErrors.OwnRequest),
                RequestStateError.AlreadyApprovedByYou => new BusinessRuleException(PurchaseRequestErrors.AlreadyApprovedByYou),
                _ => ex,
            };
        }
    }

    private static void Validate(SaveRequestInput input)
    {
        var v = new ValidationBuilder()
            .Required("title", input.Title, "Title")
            .MaxLength("title", input.Title, Domain.PurchaseRequest.MaxTitleLength, "Title")
            .MaxLength("description", input.Description, Domain.PurchaseRequest.MaxDescriptionLength, "Description");
        if (input.Lines.Count == 0)
            v.Add("lines", "validation.required", "Lines");
        if (input.Lines.Count > Domain.PurchaseRequest.MaxLines)
            v.Add("lines", "validation.maxLength", "Lines", Domain.PurchaseRequest.MaxLines);
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var line = input.Lines[i];
            v.Required($"lines[{i}].item", line.Item, "Item").MaxLength($"lines[{i}].item", line.Item, RequestLine.MaxItemLength, "Item");
            if (line.Quantity <= 0)
                v.Add($"lines[{i}].quantity", "validation.positive", "Quantity");
            if (line.UnitPrice < 0)
                v.Add($"lines[{i}].unitPrice", "validation.range", "Unit price", 0, 1_000_000_000);
        }
        v.ThrowIfAny();
    }
}

/// <summary>Document numbers from a tenant's format, for example <c>PR-{date:yyyyMMdd}-{seq:0000}</c> → <c>PR-20261009-0007</c>. The date is the tenant's business date.</summary>
public static class NumberFormatter
{
    private static readonly Regex Tokens = new(@"\{(?<name>date|seq)(:(?<format>[^}]*))?\}", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string Format(string format, DateOnly businessDate, long sequence) =>
        Tokens.Replace(format, match =>
        {
            var specifier = match.Groups["format"].Success ? match.Groups["format"].Value : null;
            return match.Groups["name"].Value == "date"
                ? businessDate.ToString(specifier ?? "yyyyMMdd", CultureInfo.InvariantCulture)
                : sequence.ToString(specifier ?? "0", CultureInfo.InvariantCulture);
        });

    /// <summary>The counter's name: the format with today's date filled in and the sequence removed, so a counter exists per format per day.</summary>
    public static string CounterName(string format, DateOnly businessDate)
    {
        var withDate = Tokens.Replace(format, match => match.Groups["name"].Value == "date"
            ? businessDate.ToString(match.Groups["format"].Success ? match.Groups["format"].Value : "yyyyMMdd", CultureInfo.InvariantCulture)
            : "#");
        return withDate.Length > 100 ? withDate[..100] : withDate;
    }
}
