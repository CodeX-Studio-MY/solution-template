using System.ComponentModel.DataAnnotations;

namespace Acme.Contracts;

/// <summary>A tenant's look: the colour of the app and its logo. The frontend builds its theme from this; nothing else changes per tenant.</summary>
[TenantSettings(ModuleKeys.TenantSettings, ExposedToUi = true)]
public sealed record BrandingSettings
{
    /// <summary>A hex colour such as <c>#1f5fbf</c>. Empty uses the platform colour.</summary>
    [TenantEditable, RegularExpression("^(#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}))?$")]
    public string PrimaryColor { get; init; } = "";

    /// <summary>The logo shown in the app bar: a path on this site or a <c>data:image/</c> URI (the policy allows images from nowhere else).</summary>
    [TenantEditable, MaxLength(200_000), RegularExpression(@"^(/[^/\s][^\s]*|data:image/(?:png|jpeg|svg\+xml|webp);base64,[A-Za-z0-9+/=]+)?$")]
    public string LogoUrl { get; init; } = "";
}
