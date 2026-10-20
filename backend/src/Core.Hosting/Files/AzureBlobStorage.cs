using Farm360.Core.Configuration;
using Farm360.Core.Files;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;

namespace Farm360.Core.Hosting.Files;

/// <summary>
/// Azure Blob Storage. In Azure it uses managed identity (an account URL, no secret) and user-delegation SAS links; locally it
/// uses Azurite through a connection string.
/// </summary>
internal sealed class AzureBlobStorage : IFileStorage, IDisposable
{
    private readonly TimeProvider _time;
    private readonly BlobServiceClient _service;
    private readonly BlobContainerClient _container;
    private readonly bool _useConnectionString;
    private readonly SemaphoreSlim _ready = new(1, 1);
    private bool _containerCreated;

    public AzureBlobStorage(IOptions<StorageOptions> options, TimeProvider time)
    {
        _time = time;
        var settings = options.Value;
        _useConnectionString = !string.IsNullOrEmpty(settings.ConnectionString);
        _service = _useConnectionString
            ? new BlobServiceClient(settings.ConnectionString)
            : new BlobServiceClient(new Uri(settings.AccountUrl ?? throw new InvalidOperationException("Storage:AccountUrl or Storage:ConnectionString is required for Azure.")), new DefaultAzureCredential());
        _container = _service.GetBlobContainerClient(settings.Container);
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await EnsureContainerAsync(cancellationToken);
        await _container.GetBlobClient(key).UploadAsync(content, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
        await _container.GetBlobClient(key).OpenReadAsync(cancellationToken: cancellationToken);

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        (await _container.GetBlobClient(key).ExistsAsync(cancellationToken)).Value;

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        await _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    public async Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        await foreach (var blob in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken))
            await _container.GetBlobClient(blob.Name).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task<Uri> CreateSignedUrlAsync(string key, TimeSpan lifetime, string? downloadName = null, CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(key);
        var expires = _time.GetUtcNow().Add(lifetime);
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, expires)
        {
            BlobContainerName = _container.Name,
            BlobName = key,
            Resource = "b",
            ContentDisposition = downloadName is null ? null : $"attachment; filename=\"{downloadName}\"",
        };
        if (_useConnectionString)
            return blob.GenerateSasUri(sas);

        // Managed identity: sign with a user-delegation key instead of an account key.
        var delegation = await _service.GetUserDelegationKeyAsync(_time.GetUtcNow().AddMinutes(-1), expires, cancellationToken);
        return new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(delegation.Value, _service.AccountName) }.ToUri();
    }

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_containerCreated)
            return;
        await _ready.WaitAsync(cancellationToken);
        try
        {
            if (!_containerCreated)
            {
                await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);   // private: no public access
                _containerCreated = true;
            }
        }
        finally
        {
            _ready.Release();
        }
    }

    public void Dispose() => _ready.Dispose();
}
