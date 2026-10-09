using System.Security.Claims;

namespace Acme.Core.Security;

/// <summary>Who is making the current request, message or job.</summary>
public interface ICurrentPrincipal
{
    bool IsAuthenticated { get; }

    /// <summary>The <c>sub</c> claim: the user, machine or service the work is attributed to.</summary>
    string? Id { get; }

    PrincipalType? Type { get; }

    /// <summary>The acting party when work is delegated (the <c>act</c> claim), for example the machine a user signed in on.</summary>
    string? ActorId { get; }

    ClaimsPrincipal? Principal { get; }
}
