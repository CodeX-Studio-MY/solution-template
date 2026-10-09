using System.ComponentModel.DataAnnotations;

namespace Acme.Core.Configuration;

[OptionsSection("Redis", "The Redis-protocol store (Valkey locally) for cache, locks, rate limits and the SignalR backplane.")]
public sealed record RedisOptions
{
    [Required, Secret]
    [Setting("StackExchange.Redis connection string.", Example = "localhost:6379")]
    public string ConnectionString { get; init; } = "";
}
