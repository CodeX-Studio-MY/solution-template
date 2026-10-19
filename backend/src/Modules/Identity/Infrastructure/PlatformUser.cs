using Microsoft.AspNetCore.Identity;

namespace Saaskit.Identity.Infrastructure;

/// <summary>A user's sign-in details (ASP.NET Core Identity). Its ID is the user's <see cref="Domain.Principal"/> ID.</summary>
public sealed class PlatformUser : IdentityUser<Guid>
{
    /// <summary>How many times in a row the account has been locked out; each lockout lasts longer.</summary>
    public int ConsecutiveLockouts { get; set; }
}
