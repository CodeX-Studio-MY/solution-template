using Acme.Core.Http;
using Acme.PurchaseRequest.Domain;

namespace Acme.PurchaseRequest.Application;

/// <summary>The module's store, as the use cases see it. EF Core stays in Infrastructure.</summary>
public interface IRequestRepository
{
    Task<Domain.PurchaseRequest?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Page<Domain.PurchaseRequest>> ListAsync(PageRequest page, RequestStatus? status, Guid? requesterId, CancellationToken cancellationToken = default);

    void Add(Domain.PurchaseRequest request);

    /// <summary>The next document number, atomically in the current transaction (never <c>count + 1</c>).</summary>
    Task<long> NextSequenceAsync(string sequenceName, CancellationToken cancellationToken = default);

    string ETagOf(Domain.PurchaseRequest request);

    /// <summary>Applies the client's <c>If-Match</c> before a change. 428 if missing; 412 on save if stale.</summary>
    void ApplyIfMatch(Domain.PurchaseRequest request, string? ifMatch);

    /// <summary>Saves an integration event in the outbox, in the same transaction as the business change.</summary>
    void Publish<TEvent>(TEvent integrationEvent)
        where TEvent : class;

    Task SaveAsync(CancellationToken cancellationToken = default);
}

/// <summary>Supplier quote files (uploaded to object storage, quarantined until scanned).</summary>
public interface IQuoteFiles
{
    Task<Guid> UploadAsync(Guid requestId, Stream content, string claimedName, CancellationToken cancellationToken = default);

    Task<Uri> DownloadUrlAsync(Guid fileId, CancellationToken cancellationToken = default);
}

/// <summary>The tenant's choices for this module. Values are data in the catalog; every one has a default so a new tenant works with zero configuration.</summary>
public sealed record PurchaseRequestSettings
{
    /// <summary>Requests below this total are approved on submit.</summary>
    public decimal AutoApproveBelow { get; init; } = 500m;

    /// <summary>The key of the approval strategy.</summary>
    public string ApprovalPolicy { get; init; } = SingleApproverPolicy.Key;

    /// <summary>How request numbers look. <c>{date:format}</c> is the business date, <c>{seq:format}</c> the day's counter.</summary>
    public string NumberFormat { get; init; } = "PR-{date:yyyyMMdd}-{seq:0000}";
}

public interface IRequestSettings
{
    Task<PurchaseRequestSettings> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Chooses the approval strategy for the current tenant.</summary>
public interface IApprovalPolicyResolver
{
    Task<IApprovalPolicy> ResolveAsync(CancellationToken cancellationToken = default);
}
