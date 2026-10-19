using System.Net.Http.Json;
using Saaskit.Core.Platform;
using Saaskit.Notification.Application;

namespace Saaskit.Notification.Infrastructure;

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

    public async Task<IReadOnlyList<Contact>> MembersOfAsync(string moduleKey, string minimumRole, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"{Platform}/module-members?module={Uri.EscapeDataString(moduleKey)}&minRole={Uri.EscapeDataString(minimumRole)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<Contact>>(cancellationToken) ?? [];
    }
}
