using System.Text;
using Acme.Core.Configuration;
using Acme.Core.Files;
using Acme.Core.Hosting.Files;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Acme.Integration.Tests;

/// <summary>Every object-storage adapter behaves the same: put, read, signed download, delete, delete by prefix.</summary>
public sealed class StorageTests(Databases databases) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IContainer? _s3;

    public async ValueTask InitializeAsync()
    {
        // The emulator needs one identity to accept signed requests, as a local development setup would.
        const string identities = """{"identities":[{"name":"test","credentials":[{"accessKey":"testkey","secretKey":"testsecret"}],"actions":["Admin","Read","Write","List","Tagging"]}]}""";
        _s3 = new ContainerBuilder("chrislusf/seaweedfs:latest")
            .WithResourceMapping(Encoding.UTF8.GetBytes(identities), "/etc/seaweedfs/s3.json")
            .WithCommand("server", "-s3", "-s3.config=/etc/seaweedfs/s3.json", "-dir=/data")
            .WithPortBinding(8333, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8333).ForPath("/").ForStatusCodeMatching(code => (int)code < 500)))
            .Build();
        await _s3.StartAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_s3 is not null)
            await _s3.DisposeAsync();
    }

    private AzureBlobStorage Azure() => new AzureBlobStorage(
        Options.Create(new StorageOptions { Provider = "azure", Container = "files", ConnectionString = databases.AzuriteConnectionString }),
        TimeProvider.System);

    private S3Storage S3() => new S3Storage(
        Options.Create(new StorageOptions
        {
            Provider = "aws",
            Container = "files",
            ServiceUrl = $"http://{_s3!.Hostname}:{_s3.GetMappedPublicPort(8333)}",
            Region = "us-east-1",
            AccessKey = "testkey",
            SecretKey = "testsecret",
        }),
        TimeProvider.System);

    private static LocalDiskStorage Local() => new LocalDiskStorage(
        Options.Create(new StorageOptions { Provider = "local", Container = "files", LocalPath = Path.Combine(Path.GetTempPath(), $"acme-storage-{Guid.NewGuid():N}") }),
        new EphemeralDataProtectionProvider(),
        TimeProvider.System);

    [Fact]
    public Task Azure_Blob_storage_works() => ExerciseAsync(Azure(), download: true);

    [Fact]
    public Task S3_compatible_storage_works() => ExerciseAsync(S3(), download: true);

    [Fact]
    public Task Local_disk_storage_works() => ExerciseAsync(Local(), download: false);

    private static async Task ExerciseAsync(IFileStorage storage, bool download)
    {
        var tenant = Guid.NewGuid();
        var key = $"{tenant}/purchase-request/purchase-request/{Guid.NewGuid()}/{Guid.NewGuid()}";
        var other = $"{Guid.NewGuid()}/purchase-request/purchase-request/{Guid.NewGuid()}/{Guid.NewGuid()}";
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7 hello");

        await storage.PutAsync(key, new MemoryStream(bytes), "application/pdf", Ct);
        await storage.PutAsync(other, new MemoryStream(bytes), "application/pdf", Ct);
        (await storage.ExistsAsync(key, Ct)).ShouldBeTrue();

        await using (var read = await storage.OpenReadAsync(key, Ct))
        {
            using var copy = new MemoryStream();
            await read.CopyToAsync(copy, Ct);
            copy.ToArray().ShouldBe(bytes);
        }

        if (download)
        {
            // A short-lived signed link downloads straight from the store, with the right name.
            var url = await storage.CreateSignedUrlAsync(key, TimeSpan.FromMinutes(5), "quote.pdf", Ct);
            using var http = new HttpClient();
            var response = await http.GetAsync(url, Ct);
            response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
            (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(bytes);
        }

        // Offboarding deletes one tenant's prefix and nothing else.
        await storage.DeletePrefixAsync($"{tenant}/", Ct);
        (await storage.ExistsAsync(key, Ct)).ShouldBeFalse();
        (await storage.ExistsAsync(other, Ct)).ShouldBeTrue();
        await storage.DeleteAsync(other, Ct);
        (await storage.ExistsAsync(other, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Local_download_links_expire_and_cannot_be_forged()
    {
        var storage = Local();
        var key = $"{Guid.NewGuid()}/m/e/{Guid.NewGuid()}/{Guid.NewGuid()}";
        await storage.PutAsync(key, new MemoryStream([1, 2, 3]), "application/octet-stream", Ct);

        var url = await storage.CreateSignedUrlAsync(key, TimeSpan.FromMinutes(1), "x.bin", Ct);
        var token = Uri.UnescapeDataString(url.OriginalString[(LocalDiskStorage.RoutePrefix.Length + 1)..]);
        storage.Resolve(token).ShouldNotBeNull();
        storage.Resolve(token + "x").ShouldBeNull();
        storage.Resolve("garbage").ShouldBeNull();

        var expired = Local();
        var past = await expired.CreateSignedUrlAsync(key, TimeSpan.FromMilliseconds(-1), null, Ct);
        expired.Resolve(Uri.UnescapeDataString(past.OriginalString[(LocalDiskStorage.RoutePrefix.Length + 1)..])).ShouldBeNull();

        // A key that leaves the storage folder is refused.
        await Should.ThrowAsync<ArgumentException>(() => storage.PutAsync("../outside", new MemoryStream([1]), "x/y", Ct));
    }

    [Fact]
    public void Content_is_recognised_by_its_bytes_not_its_name()
    {
        ContentSniffer.Detect("%PDF-1.4"u8).ShouldBe("application/pdf");
        ContentSniffer.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]).ShouldBe("image/png");
        ContentSniffer.Detect([0xFF, 0xD8, 0xFF, 0xE0]).ShouldBe("image/jpeg");
        ContentSniffer.Detect("name,price\nA,1\n"u8).ShouldBe("text/plain");
        ContentSniffer.Detect([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]).ShouldBeNull();   // a Windows program
        ContentSniffer.Matches("application/zip", ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"]).ShouldBeTrue();
        ContentSniffer.Matches("text/plain", ["text/csv"]).ShouldBeTrue();
        ContentSniffer.Matches("image/png", ["application/pdf"]).ShouldBeFalse();
    }
}
