using Acme.Core.Configuration;
using Acme.Core.Files;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Acme.Core.Hosting.Files;

public static class PlatformFiles
{
    /// <summary>Registers the object-storage adapter chosen by <c>Storage:Provider</c>.</summary>
    public static IServiceCollection AddPlatformFiles(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPlatformOptions<StorageOptions>(configuration);
        services.TryAddSingleton<IMalwareScanner, NoScanner>();
        services.AddScoped<FileService>();
        var storage = configuration.GetValidatedOptions<StorageOptions>();
        switch (storage.Provider)
        {
            case "azure":
                services.AddSingleton<IFileStorage, AzureBlobStorage>();
                break;
            case "aws":
                services.AddSingleton<IFileStorage, S3Storage>();
                break;
            case "local":
                services.AddSingleton<LocalDiskStorage>();
                services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<LocalDiskStorage>());
                break;
        }
        return services;
    }

    /// <summary>Serves the local provider's download links (development only; the other providers hand out links to the store itself).</summary>
    public static void MapLocalFileDownloads(this IEndpointRouteBuilder app)
    {
        if (app.ServiceProvider.GetService<LocalDiskStorage>() is null)
            return;
        app.MapGet($"{LocalDiskStorage.RoutePrefix}/{{token}}", (string token, LocalDiskStorage storage) =>
            storage.Resolve(token) is { } file
                ? Results.File(file.Path, file.ContentType, file.DownloadName)
                : Results.NotFound()).AllowAnonymous().ExcludeFromDescription();
    }
}
