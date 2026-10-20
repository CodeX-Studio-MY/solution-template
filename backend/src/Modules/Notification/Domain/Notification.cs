using Farm360.Core.Data;

namespace Farm360.Notification.Domain;

/// <summary>A message for one person in one tenant, kept as history with its read status. The text is in the tenant's language at the time it was made.</summary>
public sealed class UserNotification : BaseEntity
{
    public const int MaxTitleLength = 200;
    public const int MaxBodyLength = 2000;

    private UserNotification()
    {
    }

    public UserNotification(Guid recipientId, string kind, string title, string body, string? link)
    {
        RecipientId = recipientId;
        Kind = kind;
        Title = title;
        Body = body;
        Link = link;
    }

    public Guid RecipientId { get; private set; }

    /// <summary>What happened, for example <c>goods-receipt.approved</c>. The UI picks an icon from it.</summary>
    public string Kind { get; private set; } = "";

    public string Title { get; private set; } = "";

    public string Body { get; private set; } = "";

    /// <summary>A path inside the app to open, for example <c>/goods-receipt/{id}</c>.</summary>
    public string? Link { get; private set; }

    public DateTime? ReadOn { get; private set; }

    public bool IsRead => ReadOn is not null;

    public void MarkRead(DateTime utcNow) => ReadOn ??= utcNow;
}
