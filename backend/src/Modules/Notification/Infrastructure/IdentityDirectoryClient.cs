using System.Net.Http.Json;
using Acme.Core.Platform;
using Acme.Notification.Application;

namespace Acme.Notification.Infrastructure;

/// <summary>Asks the Identity module (over HTTP, with this service's token and the tenant) who to contact.</summary>
internal sealed class IdentityDirectoryClient(HttpClient http) : IContactDirectory
{
    private static readonly string Platform = $"{PlatformServices.Identity.RoutePrefix}/v1/platform";

    public async Task<Contact?> FindAsync(Guid principalId, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"{Platform}/principals/{principalId}/contact", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Contact>(cancellationToken);
    }

    public async Task<IReadOnlyList<Contact>> ApproversOfAsync(string moduleKey, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"{Platform}/module-members?module={Uri.EscapeDataString(moduleKey)}&minRole={Roles.Manager}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<Contact>>(cancellationToken) ?? [];
    }
}
