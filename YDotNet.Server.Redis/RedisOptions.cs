using StackExchange.Redis;

namespace YDotNet.Server.Redis;

public sealed class RedisOptions
{
    public ConfigurationOptions? Configuration { get; set; }

    public Func<TextWriter, Task<IConnectionMultiplexer>>? ConnectionFactory { get; set; }

    internal async Task<IConnectionMultiplexer> ConnectAsync(TextWriter log)
    {
        if (ConnectionFactory != null)
        {
            return await ConnectionFactory(log).ConfigureAwait(false);
        }

        if (Configuration != null)
        {
            return await ConnectionMultiplexer.ConnectAsync(Configuration, log).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Either configuration or connection factory must be set.");
    }
}
