using Saaskit.Core.Errors;
using Saaskit.Core.Platform;
using Saaskit.Core.Security;
using Saaskit.Identity.Application;
using Saaskit.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Saaskit.Identity.Infrastructure;

internal sealed class UserPreferences(IdentityDbContext db) : IUserPreferences
{
    public async Task<UserPreferencesDto> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        new(await db.UserPreferences.AsNoTracking().Where(p => p.UserPreferenceId == userId).Select(p => p.Locale).SingleOrDefaultAsync(cancellationToken));

    public async Task<UserPreferencesDto> SetLocaleAsync(Guid userId, string? locale, CancellationToken cancellationToken = default)
    {
        var clean = string.IsNullOrWhiteSpace(locale) ? null : locale.Trim();
        if (clean is not null && !PlatformInfo.DefaultLocales.Contains(clean))
            throw new ValidationException([new FieldError("locale", "validation.unknownOption", ["locale"])]);
        if (!await db.Principals.AnyAsync(p => p.Id == userId && p.Type == PrincipalType.User, cancellationToken))
            throw new NotFoundException(IdentityErrors.PrincipalNotFound);

        var row = await db.UserPreferences.SingleOrDefaultAsync(p => p.UserPreferenceId == userId, cancellationToken);
        if (row is null)
            db.UserPreferences.Add(new UserPreference(userId, clean));
        else
            row.SetLocale(clean);
        await db.SaveChangesAsync(cancellationToken);
        return new UserPreferencesDto(clean);
    }
}
