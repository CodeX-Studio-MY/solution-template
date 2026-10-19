using System.Text.Json;
using Saaskit.Core.Platform;
using Saaskit.Tenancy.Application;

namespace Saaskit.DevSeed;

/// <summary>
/// The local development tenant: <c>demo</c>, on the first plan in platform.yaml, with no data, and its first admin's invitation link.
/// Add sample data for your own modules here when it helps development. Safe to run again: what exists is left alone.
/// </summary>
internal sealed partial class DemoData(IServiceProvider services)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task SeedAsync(string invitationsFile, CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<DemoData>();
        var invitations = new Dictionary<string, string>(StringComparer.Ordinal);

        const string Slug = "demo";
        if (await OnboardAsync(Slug, "Demo Co", Plans.All[0], cancellationToken) is { } url)
        {
            invitations[Slug] = url;
            LogInvitation(logger, Slug, url);
        }

        await EnsureOperatorAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(invitationsFile))!, "operator.json"), cancellationToken);

        if (invitations.Count > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(invitationsFile))!);
            var existing = File.Exists(invitationsFile) ? JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(invitationsFile, cancellationToken)) ?? [] : [];
            foreach (var (slug, link) in invitations)
                existing[slug] = link;
            await File.WriteAllTextAsync(invitationsFile, JsonSerializer.Serialize(existing, Indented), cancellationToken);
        }
        LogDone(logger);
    }

    /// <summary>
    /// Onboards a tenant, or resumes an onboarding that stopped part-way (an earlier failed run); one that finished does nothing.
    /// Returns the first admin's invitation link, or null if it was sent by an earlier run.
    /// </summary>
    private async Task<string?> OnboardAsync(string slug, string name, string plan, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IOnboarding>()
            .OnboardAsync(new OnboardRequest(slug, name, plan, $"admin@{slug}.example", $"{name} Admin"), cancellationToken);
        return result.AdminInvitationUrl?.ToString();
    }

    /// <summary>
    /// A platform operator for local development, so scripts/onboard-tenant can create more tenants. Its credentials are saved next to
    /// the invitation links (git-ignored); a production operator is created with <c>Migrator --create-operator</c> and never saved.
    /// </summary>
    private async Task EnsureOperatorAsync(string file, CancellationToken cancellationToken)
    {
        if (File.Exists(file))
            return;
        await using var scope = services.CreateAsyncScope();
        try
        {
            var created = await scope.ServiceProvider.GetRequiredService<IOperatorAccounts>().CreateAsync("dev-operator@example.com", "Dev Operator", cancellationToken);
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { email = created.Email, password = created.Password, totpSecret = created.TotpSecret }, Indented), cancellationToken);
        }
        catch (Saaskit.Core.Errors.ConflictException)
        {
            // It exists from an earlier run whose credentials were lost: scripts/dev-down --reset starts clean.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "INVITATION {Tenant} {Url}")]
    private static partial void LogInvitation(ILogger logger, string tenant, string url);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data is ready")]
    private static partial void LogDone(ILogger logger);
}
