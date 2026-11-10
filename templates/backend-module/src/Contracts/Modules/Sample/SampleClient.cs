using System.Net;
using System.Net.Http.Json;

namespace Saaskit.Contracts.Sample;

/// <summary>
/// What another module may ask of this one. A module that needs it registers the client with one line in its <c>Register</c>:
/// <c>services.AddPlatformServiceClient&lt;ISampleClient, SampleClient&gt;(PlatformServices.Sample)</c>, which adds the base address
/// and the right token. Keep the methods to reads and commands other modules truly need. Replace the example with your own.
/// </summary>
public interface ISampleClient
{
    /// <summary>The item, or null if there is none with that ID.</summary>
    Task<ItemView?> GetItemAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Calls the module's HTTP API. It uses only the framework, so Contracts still references nothing.</summary>
public sealed class SampleClient(HttpClient http) : ISampleClient
{
    // The route convention, /api/{module-key} (PlatformService.RoutePrefix in Core, which Contracts can't reference).
    private const string Api = "api";

    private static readonly string Items = $"/{Api}/{ModuleKeys.Sample}/v1/items";

    public async Task<ItemView?> GetItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"{Items}/{id}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ItemView>(cancellationToken);
    }
}
