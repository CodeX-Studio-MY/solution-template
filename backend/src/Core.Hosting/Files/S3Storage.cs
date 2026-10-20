using Farm360.Core.Configuration;
using Farm360.Core.Files;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Farm360.Core.Hosting.Files;

/// <summary>
/// Amazon S3, or an S3-compatible emulator (SeaweedFS locally; MinIO is not used, because its community edition no longer
/// publishes images). In AWS the task or instance role supplies credentials.
/// </summary>
internal sealed class S3Storage : IFileStorage, IDisposable
{
    private readonly TimeProvider _time;
    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly SemaphoreSlim _ready = new(1, 1);
    private bool _bucketCreated;
    private readonly bool _emulator;

    public S3Storage(IOptions<StorageOptions> options, TimeProvider time)
    {
        _time = time;
        var settings = options.Value;
        _bucket = settings.Container;
        _emulator = !string.IsNullOrEmpty(settings.ServiceUrl);
        var config = new AmazonS3Config();
        if (_emulator)
        {
            config.ServiceURL = settings.ServiceUrl;
            config.ForcePathStyle = true;   // emulators don't have per-bucket hostnames
        }
        else if (!string.IsNullOrEmpty(settings.Region))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region);
        }
        _client = !string.IsNullOrEmpty(settings.AccessKey) && !string.IsNullOrEmpty(settings.SecretKey)
            ? new AmazonS3Client(new BasicAWSCredentials(settings.AccessKey, settings.SecretKey), config)
            : new AmazonS3Client(config);
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await EnsureBucketAsync(cancellationToken);
        await _client.PutObjectAsync(new PutObjectRequest { BucketName = _bucket, Key = key, InputStream = content, ContentType = contentType, AutoCloseStream = false }, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetObjectAsync(_bucket, key, cancellationToken);
        return response.ResponseStream;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_bucket, key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        await _client.DeleteObjectAsync(_bucket, key, cancellationToken);

    public async Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        string? token = null;
        do
        {
            var page = await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket, Prefix = prefix, ContinuationToken = token }, cancellationToken);
            foreach (var item in page.S3Objects ?? [])
                await _client.DeleteObjectAsync(_bucket, item.Key, cancellationToken);
            token = page.IsTruncated == true ? page.NextContinuationToken : null;
        }
        while (token is not null);
    }

    public Task<Uri> CreateSignedUrlAsync(string key, TimeSpan lifetime, string? downloadName = null, CancellationToken cancellationToken = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Expires = _time.GetUtcNow().UtcDateTime.Add(lifetime),
            Verb = HttpVerb.GET,
            Protocol = _emulator && _client.Config.ServiceURL?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true ? Protocol.HTTP : Protocol.HTTPS,
        };
        if (downloadName is not null)
            request.ResponseHeaderOverrides.ContentDisposition = $"attachment; filename=\"{downloadName}\"";
        return Task.FromResult(new Uri(_client.GetPreSignedURL(request)));
    }

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        if (_bucketCreated || !_emulator)
            return;   // in AWS the bucket is created by infrastructure code
        await _ready.WaitAsync(cancellationToken);
        try
        {
            if (!_bucketCreated)
            {
                try
                {
                    await _client.PutBucketAsync(_bucket, cancellationToken);
                }
                catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
                {
                    // already there
                }
                _bucketCreated = true;
            }
        }
        finally
        {
            _ready.Release();
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _ready.Dispose();
    }
}
