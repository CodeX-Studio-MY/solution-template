using Acme.Core.Data;

namespace Acme.Identity.Domain;

/// <summary>A named set of users and machines in a tenant. Everyone in a group inherits the group's module roles.</summary>
public sealed class Group : BaseEntity, IAggregateRoot, ISoftDeletable
{
    public const int MaxNameLength = 100;

    private Group()
    {
    }

    public Group(string name)
    {
        Rename(name);
        IsActive = true;
    }

    public string Name { get; private set; } = "";

    /// <summary>Uppercase invariant name, so uniqueness never depends on database collation.</summary>
    public string NormalizedName { get; private set; } = "";

    public bool IsActive { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime? DeletedOn { get; private set; }

    public void Rename(string name)
    {
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public void MarkDeleted(DateTime utcNow)
    {
        IsDeleted = true;
        DeletedOn = utcNow;
        IsActive = false;
    }
}

/// <summary>A principal's membership of a group. Service principals are never put in groups.</summary>
public sealed class GroupMembership : BaseEntity
{
    private GroupMembership()
    {
    }

    public GroupMembership(Guid groupId, Guid principalId)
    {
        GroupId = groupId;
        PrincipalId = principalId;
        IsActive = true;
    }

    public Guid GroupId { get; private set; }

    public Guid PrincipalId { get; private set; }

    public bool IsActive { get; private set; }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
