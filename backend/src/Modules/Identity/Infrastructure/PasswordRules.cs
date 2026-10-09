using System.Security.Cryptography;
using System.Text;
using Acme.Core.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.Identity.Infrastructure;

/// <summary>
/// Password rules following NIST SP 800-63B-4: at least 15 characters when the password is the only factor (8 with MFA),
/// at least 64 allowed, no composition rules, no forced periodic changes, and breached passwords are rejected.
/// </summary>
internal sealed partial class NistPasswordValidator(
    IHttpClientFactory clients,
    IOptions<TokenServerOptions> options,
    ILogger<NistPasswordValidator> logger) : IPasswordValidator<PlatformUser>
{
    public const int MinimumLengthWithoutMfa = 15;
    public const int MinimumLengthWithMfa = 8;
    public const int MaximumLength = 256;

    public async Task<IdentityResult> ValidateAsync(UserManager<PlatformUser> manager, PlatformUser user, string? password)
    {
        var errors = new List<IdentityError>();
        var hasMfa = user.TwoFactorEnabled;
        var minimum = hasMfa ? MinimumLengthWithMfa : MinimumLengthWithoutMfa;
        if (string.IsNullOrEmpty(password) || password.Length < minimum)
            errors.Add(new IdentityError { Code = "PasswordTooShort", Description = $"Passwords need at least {minimum} characters." });
        else if (password.Length > MaximumLength)
            errors.Add(new IdentityError { Code = "PasswordTooLong", Description = $"Passwords can have at most {MaximumLength} characters." });
        else if (options.Value.BreachedPasswordCheck && await IsBreachedAsync(password))
            errors.Add(new IdentityError { Code = "PasswordBreached", Description = "This password appears in a known data breach. Choose another." });
        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }

    /// <summary>Have I Been Pwned k-anonymity range API: only the first five hash characters leave the server.</summary>
    private async Task<bool> IsBreachedAsync(string password)
    {
#pragma warning disable CA5350 // SHA-1 is what the Have I Been Pwned range API requires; it is not used for security here.
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350
        try
        {
            var client = clients.CreateClient(HttpClientName);
            var body = await client.GetStringAsync(new Uri($"range/{hash[..5]}", UriKind.Relative));
            return body.Split('\n').Any(line => line.StartsWith(hash[5..], StringComparison.OrdinalIgnoreCase));
        }
        catch (HttpRequestException ex)
        {
            // Availability of a third party must not block sign-up; the length rules still apply.
            LogBreachCheckUnavailable(logger, ex);
            return false;
        }
    }

    public const string HttpClientName = "pwned-passwords";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Breached-password check unavailable")]
    private static partial void LogBreachCheckUnavailable(ILogger logger, Exception exception);
}
