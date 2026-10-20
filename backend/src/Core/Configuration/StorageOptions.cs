using System.ComponentModel.DataAnnotations;

namespace Farm360.Core.Configuration;

[OptionsSection("Storage", "Object storage for uploaded files. Services are stateless: files never live on a container's disk.")]
public sealed record StorageOptions
{
    [Required, AllowedValues("azure", "aws", "local")]
    [Setting("Which store: azure (Blob), aws (S3 or an S3-compatible emulator), or local (a folder, for development only).", Example = "azure")]
    public string Provider { get; init; } = "";

    [Required]
    [Setting("Container (Azure) or bucket (S3) holding every tenant's files. Object keys start with {tenantId}/.")]
    public string Container { get; init; } = "files";

    [Secret]
    [Setting("Azure Blob connection string (local development with Azurite). Leave empty in the cloud and set AccountUrl, so managed identity is used.")]
    public string? ConnectionString { get; init; }

    [Url]
    [Setting("Azure Blob account URL, for managed identity (no secret).", Example = "https://myaccount.blob.core.windows.net")]
    public string? AccountUrl { get; init; }

    [Url]
    [Setting("S3 service URL, only for an S3-compatible emulator such as SeaweedFS. Leave empty for AWS.", Example = "http://seaweedfs:8333")]
    public string? ServiceUrl { get; init; }

    [Setting("S3 region.", Example = "ap-southeast-1")]
    public string? Region { get; init; }

    [Secret]
    [Setting("S3 access key, for an emulator. In AWS, the task or instance role is used.")]
    public string? AccessKey { get; init; }

    [Secret]
    [Setting("S3 secret key, for an emulator.")]
    public string? SecretKey { get; init; }

    [Setting("Folder for the local provider. Development only.", Example = "/tmp/farm360-files")]
    public string? LocalPath { get; init; }

    [Range(1, 1024)]
    [Setting("The largest upload accepted, in megabytes.")]
    public int MaxUploadMegabytes { get; init; } = 10;

    [Range(1, 1440)]
    [Setting("How long a download link works, in minutes. Links are signed and short-lived.")]
    public int SignedUrlMinutes { get; init; } = 10;
}
