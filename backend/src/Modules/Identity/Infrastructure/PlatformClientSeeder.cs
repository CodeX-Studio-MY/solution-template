using Acme.Core.Configuration;
using Acme.Core.Platform;
using Acme.Identity.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Acme.Identity.Infrastructure;

/// <summary>
/// At start-up, registers the platform's own OAuth clients from configuration (secrets come from the secret store):
/// the Gateway's BFF client, and each service's client, which may call other services and exchange user tokens.
/// Idempotent. A client with no configured secret is not registered.
/// </summary>
internal sealed class PlatformClientSeeder(IServiceScopeFactory scopes, IOptions<TokenServerOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var clients = scope.ServiceProvider.GetRequiredService<IClientRegistry>();
        var secrets = options.Value.PlatformClientSecrets;

        if (secrets.TryGetValue(PlatformClients.Bff, out var bffSecret) && !string.IsNullOrEmpty(bffSecret))
            await clients.RegisterIfChangedAsync(PlatformClients.Bff, "Gateway (BFF)", bffSecret, ["authorization_code", "refresh_token"], cancellationToken);

        foreach (var service in PlatformServices.All)
        {
            if (secrets.TryGetValue(service.ClientId, out var secret) && !string.IsNullOrEmpty(secret))
                await clients.RegisterIfChangedAsync(service.ClientId, service.Key, secret, ["client_credentials", "urn:ietf:params:oauth:grant-type:token-exchange"], cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
