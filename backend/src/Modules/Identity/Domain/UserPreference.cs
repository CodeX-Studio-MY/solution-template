namespace Saaskit.Identity.Domain;

/// <summary>
/// One user's preferences in this tenant's database. A row exists only once someone sets a preference. The key is the user's ID
/// (their <see cref="Principal"/>, which is also their sign-in details' ID).
/// Real columns, not a JSON blob: a future preference such as time zone is one new column.
/// </summary>
public sealed class UserPreference
{
    public const int MaxLocaleLength = 16;

    private UserPreference()
    {
    }

    public UserPreference(Guid userId, string? locale)
    {
        UserPreferenceId = userId;
        Locale = locale;
    }

    public Guid UserPreferenceId { get; private set; }

    /// <summary>One of the platform's locales. Empty means use the tenant's language.</summary>
    public string? Locale { get; private set; }

    public void SetLocale(string? locale) => Locale = locale;
}
