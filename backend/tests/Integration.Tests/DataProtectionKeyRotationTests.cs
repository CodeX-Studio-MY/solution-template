using System.Security.Cryptography;
using System.Xml.Linq;
using Saaskit.Core.Hosting.DataProtection;

namespace Saaskit.Integration.Tests;

/// <summary>The key-encryption key of the shared Data Protection ring can be rotated without signing everyone out.</summary>
public sealed class DataProtectionKeyRotationTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static byte[] Encrypt(string key, XElement element) =>
        Convert.FromBase64String(new AesGcmXmlEncryptor(Convert.FromBase64String(key)).Encrypt(element).EncryptedElement.Element("value")!.Value);

    [Fact]
    public void An_entry_written_under_the_old_key_is_read_while_the_old_key_is_still_listed()
    {
        var oldKey = NewKey();
        var newKey = NewKey();
        var element = new XElement("key", new XAttribute("id", "1"), "secret");
        var written = Encrypt(oldKey, element);

        AesGcmXmlDecryptor.Decrypt(written, [newKey, oldKey]).ToString().ShouldBe(element.ToString());
        AesGcmXmlDecryptor.Decrypt(Encrypt(newKey, element), [newKey, oldKey]).ToString().ShouldBe(element.ToString());
    }

    [Fact]
    public void An_entry_under_a_key_that_is_no_longer_listed_is_refused_with_an_explanation()
    {
        var written = Encrypt(NewKey(), new XElement("key", "secret"));
        var error = Should.Throw<CryptographicException>(() => AesGcmXmlDecryptor.Decrypt(written, [NewKey()]));
        error.Message.ShouldContain("PreviousKeyEncryptionKeys");
    }
}
