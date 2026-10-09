using System.Security.Cryptography;
using Aspire.Hosting.Publishing;

namespace Acme.AppHost;

/// <summary>
/// Local-development secrets, generated on first run and kept in the AppHost's user secrets (never in the repository).
/// They stay the same between runs, because the Data Protection key ring in Valkey is encrypted with one of them.
/// </summary>
internal sealed class EcSigningKeyDefault : ParameterDefault
{
    /// <summary>An ES256 (P-256) private key in PEM form.</summary>
    public override string GetDefaultValue()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportECPrivateKeyPem();
    }

    public override void WriteToManifest(ManifestPublishingContext context)
    {
        context.Writer.WriteStartObject("generate");
        context.Writer.WriteString("kind", "ec-p256-pem");
        context.Writer.WriteEndObject();
    }
}

/// <summary>A random 256-bit key, base64 encoded.</summary>
internal sealed class Base64KeyDefault : ParameterDefault
{
    public override string GetDefaultValue() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public override void WriteToManifest(ManifestPublishingContext context)
    {
        context.Writer.WriteStartObject("generate");
        context.Writer.WriteString("kind", "base64-256");
        context.Writer.WriteEndObject();
    }
}
