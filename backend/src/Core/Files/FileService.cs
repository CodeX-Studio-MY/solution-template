using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Acme.Core.Configuration;
using Acme.Core.Data;
using Acme.Core.Errors;
using Acme.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Acme.Core.Files;

/// <summary>What the caller wants stored. The key and the stored name come from the server, never from this.</summary>
/// <param name="ModuleKey">The module that owns the file.</param>
/// <param name="Entity">The kind of record it belongs to, for the key (for example <c>purchase-request</c>).</param>
/// <param name="EntityId">The record it belongs to.</param>
/// <param name="Content">The uploaded bytes.</param>
/// <param name="ClaimedName">The name the client gave. Used for display after sanitising; never for storage.</param>
/// <param name="AllowedContentTypes">The content types this upload accepts, checked against the file's real bytes.</param>
public sealed record FileUpload(string ModuleKey, string Entity, Guid EntityId, Stream Content, string ClaimedName, IReadOnlyCollection<string> AllowedContentTypes);

/// <summary>
/// Accepts uploads: checks the size limit and the real content type (magic bytes), stores the object under a server-generated key
/// <c>{tenantId}/{module-key}/{entity}/{id}/{fileId}</c>, and keeps it quarantined until the malware scan says it is clean.
/// </summary>
public sealed partial class FileService(
    IFileStorage storage,
    IMalwareScanner scanner,
    ITenantContext tenant,
    IOptions<StorageOptions> options)
{
    private const int SniffBytes = 4096;

    public async Task<StoredFile> UploadAsync(ModuleDbContext db, FileUpload upload, CancellationToken cancellationToken = default)
    {
        var limit = options.Value.MaxUploadMegabytes * 1024L * 1024L;
        using var buffer = new MemoryStream();
        await using (var limited = new LimitedStream(upload.Content, limit))
            await limited.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        var detected = ContentSniffer.Detect(bytes.AsSpan(0, Math.Min(bytes.Length, SniffBytes)));
        if (detected is null || !ContentSniffer.Matches(detected, upload.AllowedContentTypes))
            throw new ValidationException([new FieldError("file", "validation.fileType", string.Join(", ", upload.AllowedContentTypes))]);

        var fileId = Guid.CreateVersion7();
        var key = $"{tenant.Tenant.Id}/{upload.ModuleKey}/{upload.Entity}/{upload.EntityId}/{fileId}";
        var contentType = ResolveContentType(detected, upload.AllowedContentTypes, upload.ClaimedName);
        var file = new StoredFile(key, contentType, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), SafeDisplayName(upload.ClaimedName));

        await storage.PutAsync(key, new MemoryStream(bytes), contentType, cancellationToken);
        switch (await scanner.ScanAsync(key, new MemoryStream(bytes), cancellationToken))
        {
            case ScanResult.Clean:
                file.MarkClean();
                break;
            case ScanResult.Infected:
                await storage.DeleteAsync(key, cancellationToken);
                throw new ValidationException([new FieldError("file", "validation.fileInfected")]);
            case ScanResult.Pending:
                break;   // stays quarantined
        }
        db.StoredFiles.Add(file);
        return file;
    }

    /// <summary>A short-lived download link. Files that are still quarantined, or were rejected, can't be downloaded.</summary>
    public async Task<Uri> GetDownloadUrlAsync(ModuleDbContext db, Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            ?? throw new NotFoundException();
        if (file.Status != StoredFileStatus.Clean)
            throw new BusinessRuleException(null, "The file has not passed the security scan.");
        return await storage.CreateSignedUrlAsync(file.Key, TimeSpan.FromMinutes(options.Value.SignedUrlMinutes), file.DisplayName, cancellationToken);
    }

    /// <summary>Called by the scanner's callback when a pending scan finishes.</summary>
    public async Task CompleteScanAsync(ModuleDbContext db, Guid fileId, ScanResult result, CancellationToken cancellationToken = default)
    {
        var file = await db.StoredFiles.SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken) ?? throw new NotFoundException();
        if (result == ScanResult.Clean)
        {
            file.MarkClean();
        }
        else if (result == ScanResult.Infected)
        {
            file.Reject();
            await storage.DeleteAsync(file.Key, cancellationToken);
        }
    }

    private static string ResolveContentType(string detected, IReadOnlyCollection<string> allowed, string claimedName)
    {
        if (allowed.Contains(detected))
            return detected;
        // A zip or OLE container: use the allowed type that the file extension names, else the first that fits.
        var extension = Path.GetExtension(claimedName).ToLowerInvariant();
        return allowed.FirstOrDefault(a => ExtensionTypes.TryGetValue(extension, out var t) && t == a) ?? allowed.First();
    }

    private static readonly Dictionary<string, string> ExtensionTypes = new(StringComparer.Ordinal)
    {
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".csv"] = "text/csv",
        [".doc"] = "application/msword",
        [".xls"] = "application/vnd.ms-excel",
    };

    /// <summary>Strips paths and anything but letters, digits, dots, dashes, underscores and spaces.</summary>
    internal static string SafeDisplayName(string claimed)
    {
        var name = Path.GetFileName(claimed.Replace('\\', '/'));
        name = UnsafeCharacters().Replace(name, "_").Trim('.', ' ');
        return string.IsNullOrEmpty(name) ? "file" : name.Length > 100 ? name[..100] : name;
    }

    [GeneratedRegex(@"[^\p{L}\p{N}._\- ]")]
    private static partial Regex UnsafeCharacters();

    /// <summary>Stops reading at the limit, so a huge upload fails early instead of filling memory.</summary>
    private sealed class LimitedStream(Stream inner, long limit) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await inner.ReadAsync(buffer, cancellationToken);
            _read += count;
            if (_read > limit)
                throw new ValidationException([new FieldError("file", "validation.fileTooLarge", limit / (1024 * 1024))]);
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
