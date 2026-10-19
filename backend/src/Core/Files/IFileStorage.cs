namespace Saaskit.Core.Files;

/// <summary>
/// Object storage. Adapters exist for Azure Blob, S3 and a local folder (development only). Services never read files from local
/// disk to answer a request: downloads use short-lived signed URLs that go straight to the store.
/// </summary>
public interface IFileStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Deletes everything under a prefix (a tenant's files when it is offboarded).</summary>
    Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>A link that downloads the object and expires after <paramref name="lifetime"/>.</summary>
    Task<Uri> CreateSignedUrlAsync(string key, TimeSpan lifetime, string? downloadName = null, CancellationToken cancellationToken = default);
}

public enum ScanResult
{
    Clean,
    Infected,

    /// <summary>The scan hasn't finished. The file stays quarantined.</summary>
    Pending,
}

/// <summary>
/// The malware-scan hook. Implement it with Defender for Storage, ClamAV or another scanner. Until a file is scanned clean it is
/// quarantined and can't be downloaded.
/// </summary>
public interface IMalwareScanner
{
    Task<ScanResult> ScanAsync(string key, Stream content, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default scanner: accepts everything, because no scanner is configured. Replace it before taking real uploads
/// (it is recorded in docs/deviations.md and the runbooks).
/// </summary>
internal sealed class NoScanner : IMalwareScanner
{
    public Task<ScanResult> ScanAsync(string key, Stream content, CancellationToken cancellationToken = default) => Task.FromResult(ScanResult.Clean);
}
