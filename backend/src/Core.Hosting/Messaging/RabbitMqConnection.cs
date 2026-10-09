using Acme.Core.Configuration;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Acme.Core.Hosting.Messaging;

/// <summary>One shared, auto-recovering connection. It is opened on first use, so a host starts even if the broker is briefly down.</summary>
internal sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
            return _connection;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
                return _connection;
            var factory = new ConnectionFactory
            {
                Uri = new Uri(options.Value.ConnectionString),
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                ClientProvidedName = Platform.PlatformInfo.Product,
            };
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Declares the exchanges every host needs. Safe to call repeatedly.</summary>
    public static async Task DeclareExchangesAsync(IChannel channel, CancellationToken cancellationToken)
    {
        var quorum = new Dictionary<string, object?> { ["x-queue-type"] = "quorum" };
        await channel.ExchangeDeclareAsync(RabbitMqTopology.UnroutedExchange, ExchangeType.Fanout, durable: true, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(RabbitMqTopology.UnroutedQueue, durable: true, exclusive: false, autoDelete: false, arguments: quorum, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(RabbitMqTopology.UnroutedQueue, RabbitMqTopology.UnroutedExchange, string.Empty, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            RabbitMqTopology.Exchange,
            ExchangeType.Topic,
            durable: true,
            arguments: new Dictionary<string, object?> { ["alternate-exchange"] = RabbitMqTopology.UnroutedExchange },
            cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(RabbitMqTopology.DeadLetterExchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
