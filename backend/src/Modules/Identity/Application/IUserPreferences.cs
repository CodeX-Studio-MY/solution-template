namespace Saaskit.Identity.Application;

/// <summary>A signed-in user's preferences. <paramref name="Locale"/> is null until they choose a language.</summary>
public sealed record UserPreferencesDto(string? Locale);

/// <summary>The signed-in user's own preferences (language so far). Language order: the user's, then the tenant's, then the platform's.</summary>
public interface IUserPreferences
{
    Task<UserPreferencesDto> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Saves the language: one of the platform's locales, or null to follow the tenant's.</summary>
    Task<UserPreferencesDto> SetLocaleAsync(Guid userId, string? locale, CancellationToken cancellationToken = default);
}
