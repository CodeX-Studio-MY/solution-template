using Acme.Core.Data;

namespace Acme.Tenancy.Domain;

/// <summary>The steps of onboarding a tenant, in order. A run resumes at the first step that hasn't completed.</summary>
public enum OnboardingStep
{
    CatalogRow = 0,
    Database = 1,
    Migrations = 2,
    ReferenceData = 3,
    Modules = 4,
    Activate = 5,
    BffRedirects = 6,
    FirstAdminInvitation = 7,
    Done = 8,
}

/// <summary>One onboarding of one tenant, recorded step by step so it is idempotent and can resume after a failure.</summary>
public sealed class OnboardingRun : BaseEntity
{
    private OnboardingRun()
    {
    }

    public OnboardingRun(string slug, string displayName, string plan, string adminEmail, string adminName, string serverPool)
    {
        Slug = slug;
        DisplayName = displayName;
        Plan = plan;
        AdminEmail = adminEmail;
        AdminName = adminName;
        ServerPool = serverPool;
    }

    public string Slug { get; private set; } = "";

    public string DisplayName { get; private set; } = "";

    public string Plan { get; private set; } = "";

    public string AdminEmail { get; private set; } = "";

    public string AdminName { get; private set; } = "";

    public string ServerPool { get; private set; } = "";

    public Guid? TenantId { get; private set; }

    /// <summary>The next step to run.</summary>
    public OnboardingStep NextStep { get; private set; }

    public string? Error { get; private set; }

    public bool IsDone => NextStep == OnboardingStep.Done;

    public void Attach(Guid tenantId) => TenantId = tenantId;

    public void Completed(OnboardingStep step)
    {
        NextStep = step + 1;
        Error = null;
    }

    public void Failed(string error) => Error = error.Length > 2000 ? error[..2000] : error;
}
