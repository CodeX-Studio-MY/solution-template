using System.ComponentModel.DataAnnotations;

namespace Farm360.Core.Configuration;

[OptionsSection("Bff", "The Gateway acting as the web app's backend-for-frontend (RFC 10017): sessions, tokens and static files.")]
public sealed record BffOptions
{
    [Required, Secret]
    [Setting("The Gateway's OAuth client secret (a confidential client). Must match TokenServer:PlatformClientSecrets for the BFF client.")]
    public string ClientSecret { get; init; } = "";

    [Range(1, 30)]
    [Setting("How long a browser session lasts without activity, in days. It matches the refresh token's sliding lifetime.")]
    public int SessionDays { get; init; } = 7;

    [Url]
    [Setting("When every service runs in one process (AllInOne.Host), route all /api traffic to this address instead of one host per service.", Example = "http://allinone")]
    public string? AllInOneAddress { get; init; }

    [Setting("Directory with the built shell app (index.html and its assets), served by the Gateway on every tenant host. Empty disables it.", Example = "/app/shell")]
    public string ShellRoot { get; init; } = "";

    [Setting("Directory with the built frontend modules, served at /mfe/{module-key}/{version}/. Empty disables it (a CDN serves them).", Example = "/app/mfe")]
    public string ModulesRoot { get; init; } = "";

    [Setting("Extra origins the browser may connect to or load scripts from, besides the app itself and the module CDN (space separated).", Example = "https://cdn.example.com")]
    public string ExtraSources { get; init; } = "";
}

[OptionsSection("DataProtection", "Protects BFF sessions, antiforgery tokens and stored secrets. The key ring is shared in Redis, wrapped by a key from the secret store.")]
public sealed record KeyRingOptions
{
    [Required, Secret]
    [Setting("Key-encryption key (256 bits, base64). New keys in the ring in Redis are encrypted with it. To rotate it, put the new key here and the old one in PreviousKeyEncryptionKeys.")]
    public string KeyEncryptionKey { get; init; } = "";

    [Secret]
    [Setting("Earlier key-encryption keys (base64), used only to read ring entries written under them. Remove one once the ring entries it protects have expired (90 days after the last rotation).")]
    public List<string> PreviousKeyEncryptionKeys { get; init; } = [];

    [Required]
    [Setting("The application name that keys are isolated by. The same for every service that must share the ring.")]
    public string ApplicationName { get; init; } = "platform";
}
