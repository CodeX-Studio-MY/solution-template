using Farm360.Core.Configuration;
using Farm360.Core.Files;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Farm360.Core.Hosting.Files;

/// <summary>
/// A folder on disk. **Development only**: containers have no persistent disk. Download links are protected, time-limited tokens
/// served by <c>/files/local/{token}</c>, so the application path is the same as with a real store.
/// </summary>
internal sealed class LocalDiskStorage : IFileStorage
{
    private readonly TimeProvider _time;
    public const string RoutePrefix = "/files/local";

    private readonly string _root;
    private readonly ITimeLimitedDataProtector _protector;

    public LocalDiskStorage(IOptions<StorageOptions> options, IDataProtectionProvider protection, TimeProvider time)
    {
        _time = time;
        _root = Path.GetFullPath(options.Value.LocalPath ?? throw new InvalidOperationException("Storage:LocalPath is required for the local provider."));
        Directory.CreateDirectory(_root);
        _protector = protection.CreateProtector("storage.local.download").ToTimeLimitedDataProtector();
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, cancellationToken);
        await File.WriteAllTextAsync(path + ".type", contentType, cancellationToken);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(File.OpenRead(PathFor(key)));

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(File.Exists(PathFor(key)));

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        File.Delete(path);
        File.Delete(path + ".type");
        return Task.CompletedTask;
    }

    public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var path = PathFor(prefix.TrimEnd('/'));
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        return Task.CompletedTask;
    }

    public Task<Uri> CreateSignedUrlAsync(string key, TimeSpan lifetime, string? downloadName = null, CancellationToken cancellationToken = default)
    {
        var token = _protector.Protect($"{key}\n{downloadName}", _time.GetUtcNow().Add(lifetime));
        return Task.FromResult(new Uri($"{RoutePrefix}/{Uri.EscapeDataString(token)}", UriKind.Relative));
    }

    /// <summary>Resolves a download token to its file, or null if it is invalid or expired.</summary>
    public (string Path, string ContentType, string? DownloadName)? Resolve(string token)
    {
        string payload;
        try
        {
            payload = _protector.Unprotect(token);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
        var parts = payload.Split('\n', 2);
        var path = PathFor(parts[0]);
        if (!File.Exists(path))
            return null;
        var type = File.Exists(path + ".type") ? File.ReadAllText(path + ".type") : "application/octet-stream";
        return (path, type, string.IsNullOrEmpty(parts[1]) ? null : parts[1]);
    }

    /// <summary>Keys are server-generated, but a path that leaves the root is refused anyway.</summary>
    private string PathFor(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));
        return path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || path == _root
            ? path
            : throw new ArgumentException("The key points outside the storage folder.", nameof(key));
    }
}
