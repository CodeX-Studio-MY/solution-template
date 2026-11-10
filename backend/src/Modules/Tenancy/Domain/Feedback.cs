using Saaskit.Core.Data;

namespace Saaskit.Tenancy.Domain;

// Stored as tinyint: the values are part of the schema, so never renumber them.
public enum FeedbackKind : byte
{
    Bug = 1,
    Idea = 2,
    Other = 3,
}

public enum FeedbackStatus : byte
{
    New = 1,
    Reviewed = 2,
    Done = 3,
}

/// <summary>
/// What a tenant's user told us, kept in the catalog because the operator reads it across tenants. The sender's name is a snapshot:
/// the operator can't read the tenant's database.
/// </summary>
public sealed class Feedback : BaseEntity, IAggregateRoot
{
    public const int MaxSenderNameLength = 100;
    public const int MaxMessageLength = 2000;
    public const int MaxPagePathLength = 300;

    private Feedback()
    {
    }

    public Feedback(Guid tenantId, Guid senderId, string senderName, FeedbackKind kind, byte? rating, string message, string pagePath)
    {
        TenantId = tenantId;
        SenderId = senderId;
        SenderName = senderName;
        Kind = kind;
        Rating = rating;
        Message = message;
        PagePath = pagePath;
        Status = FeedbackStatus.New;
    }

    public Guid TenantId { get; private set; }

    public Guid SenderId { get; private set; }

    public string SenderName { get; private set; } = "";

    public FeedbackKind Kind { get; private set; }

    public byte? Rating { get; private set; }

    public string Message { get; private set; } = "";

    public string PagePath { get; private set; } = "";

    public FeedbackStatus Status { get; private set; }

    public void SetStatus(FeedbackStatus status) => Status = status;
}
