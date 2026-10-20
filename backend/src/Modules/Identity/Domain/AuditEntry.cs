using Farm360.Core.Data;

namespace Farm360.Identity.Domain;

/// <summary>Kinds of security event recorded in the tenant's audit log.</summary>
public static class AuditKinds
{
    public const string SignIn = "sign-in";
    public const string SignInFailed = "sign-in-failed";
    public const string TokenIssued = "token-issued";
    public const string DelegatedTokenIssued = "delegated-token-issued";
    public const string AssignmentChanged = "assignment-changed";
    public const string GroupChanged = "group-changed";
    public const string MembershipChanged = "membership-changed";
    public const string PrincipalChanged = "principal-changed";
    public const string MachineEnrolled = "machine-enrolled";
    public const string KeyRotated = "key-rotated";
    public const string InvitationCreated = "invitation-created";
    public const string InvitationAccepted = "invitation-accepted";
    public const string PasswordReset = "password-reset";
    public const string SupportAccess = "support-access";
}

/// <summary>An append-only audit record: who, what, when, from where, and the trace ID. Never updated or deleted.</summary>
public sealed class AuditEntry : IAppendOnly
{
    private AuditEntry()
    {
    }

    public AuditEntry(DateTime occurredOn, string kind, string? actorId, string? actorType, string? onBehalfOf, string? subject, string? details, string? remoteAddress, string? traceId)
    {
        OccurredOn = occurredOn;
        Kind = kind;
        ActorId = actorId;
        ActorType = actorType;
        OnBehalfOf = onBehalfOf;
        Subject = subject;
        Details = details;
        RemoteAddress = remoteAddress;
        TraceId = traceId;
    }

    public long Id { get; private set; }

    public DateTime OccurredOn { get; private set; }

    public string Kind { get; private set; } = "";

    /// <summary>Who did it (the token's <c>sub</c>, or the person signing in).</summary>
    public string? ActorId { get; private set; }

    public string? ActorType { get; private set; }

    /// <summary>The acting party when delegated: the machine a user signed in on, or the operator using support access.</summary>
    public string? OnBehalfOf { get; private set; }

    /// <summary>What it was done to (a principal, group or assignment ID).</summary>
    public string? Subject { get; private set; }

    /// <summary>JSON details. Never secrets, tokens or passwords.</summary>
    public string? Details { get; private set; }

    public string? RemoteAddress { get; private set; }

    public string? TraceId { get; private set; }
}
