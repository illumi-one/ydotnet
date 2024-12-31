using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDotNet.Document;
using YDotNet.Document.Transactions;
using YDotNet.Server.Internal;
using YDotNet.Server.Storage;
using IDocumentCallbacks = System.Collections.Generic.IEnumerable<YDotNet.Server.IDocumentCallback>;

#pragma warning disable IDE0063 // Use simple 'using' statement

namespace YDotNet.Server;

public sealed class DefaultDocumentManager : IDocumentManager
{
    private readonly ConnectedUsers users = new();
    private readonly DocumentManagerOptions options;
    private readonly DocumentCache cache;
    private readonly CallbackInvoker callback;
    private ILogger<DefaultDocumentManager> _logger;

    public DefaultDocumentManager(
        IDocumentStorage documentStorage,
        IDocumentCallbacks callbacks,
        IOptions<DocumentManagerOptions> options,
        ILogger<DefaultDocumentManager> logger)
    {
        _logger = logger;
        this.options = options.Value;
        callback = new CallbackInvoker(callbacks, logger);

        cache = new DocumentCache(documentStorage, callback, this, options.Value, logger);
    }

    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        await callback.OnInitializedAsync(this).ConfigureAwait(false);
    }

    public async Task StopAsync(
        CancellationToken cancellationToken)
    {
        await cache.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask<byte[]> GetStateVectorAsync(
        DocumentContext context,
        CancellationToken ct = default)
    {
        return await cache.ApplyUpdateReturnAsync(context.DocumentName, doc =>
        {
            using (var transaction = doc.ReadTransaction())
            {
                return Task.FromResult(transaction.StateVectorV1());
            }
        }).ConfigureAwait(false);
    }

    public async ValueTask<byte[]> GetUpdateAsync(DocumentContext context, byte[] stateVector, CancellationToken ct = default)
    {
        return await cache.ApplyUpdateReturnAsync(context.DocumentName, doc =>
        {
            using (var transaction = doc.ReadTransaction())
            {
                return Task.FromResult(transaction.StateDiffV1(stateVector));
            }
        }).ConfigureAwait(false);
    }

    public async ValueTask<UpdateResult> ApplyUpdateAsync(DocumentContext context, byte[] stateDiff, CancellationToken ct = default)
    {
        return await cache.ApplyUpdateReturnAsync(context.DocumentName, async doc =>
        {
            var result = new UpdateResult
            {
                Diff = stateDiff,
            };
            var updatHash = stateDiff.GetBase64Part();
            
            using (var transaction = doc.WriteTransaction())
            {
                _logger.LogTrace("ApplyingV1 update {hash} to document {name} state: {updateState}", updatHash, context.DocumentName, transaction.GetSnapshotHash());
                result.TransactionUpdateResult = transaction.ApplyV1(stateDiff);
            }
            
            _logger.LogDebug("Invoking callback after Applied V1 update {hash} to document {name}.", updatHash, context.DocumentName);

            await callback.OnDocumentChangedAsync(new DocumentChangedEvent
            {
                Context = context,
                Diff = result.Diff,
                Document = doc,
                Source = this,
            }).ConfigureAwait(false);
            
            _logger.LogDebug("Applied V1 update {hash} to document {name}.", updatHash, context.DocumentName);

            return result;
        }).ConfigureAwait(false);
    }



    public async ValueTask UpdateDocAsync(DocumentContext context, Action<Doc> action, CancellationToken ct = default)
    {
        await cache.ApplyUpdateReturnAsync(context.DocumentName, async doc =>
        {
            using var subscribeOnce = new SubscribeToUpdatesV1Once(doc);

            action(doc);

            if (subscribeOnce.Update == null)
            {
                return false;
            }

            await callback.OnDocumentChangedAsync(new DocumentChangedEvent
            {
                Context = context,
                Diff = subscribeOnce.Update,
                Document = doc,
                Source = this,
            }).ConfigureAwait(false);

            return true;
        }).ConfigureAwait(false);
    }

    public async ValueTask PingAsync(DocumentContext context, ulong clock, string? state = null, CancellationToken ct = default)
    {
        if (users.AddOrUpdate(context.DocumentName, context.ClientId, clock, state, out var newState))
        {
            await callback.OnAwarenessUpdatedAsync(new ClientAwarenessEvent
            {
                Context = context,
                ClientClock = clock,
                ClientState = newState,
                Source = this,
            }).ConfigureAwait(false);
        }
    }

    public async ValueTask DisconnectAsync(
        DocumentContext context,
        CancellationToken ct = default)
    {
        if (users.Remove(context.DocumentName, context.ClientId))
        {
            await callback.OnClientDisconnectedAsync(new ClientDisconnectedEvent
            {
                Context = context,
                Source = this,
                Reason = DisconnectReason.Disconnect,
            }).ConfigureAwait(false);
        }
    }

    public async ValueTask CleanupAsync(
        CancellationToken ct = default)
    {
        foreach (var (clientId, documentName) in users.Cleanup(options.MaxPingTime))
        {
            await callback.OnClientDisconnectedAsync(new ClientDisconnectedEvent
            {
                Context = new DocumentContext(documentName, clientId),
                Source = this,
                Reason = DisconnectReason.Cleanup,
            }).ConfigureAwait(false);
        }

        await cache.RemoveEvictedItemsAsync().ConfigureAwait(false);
    }

    public ValueTask EvictDocAsync(DocumentContext context, CancellationToken ct = default)
    {
        return cache.EvictItem(context.DocumentName);
    }

    public async ValueTask<IReadOnlyDictionary<ulong, ConnectedUser>> GetAwarenessAsync(
        DocumentContext context,
        CancellationToken ct = default)
    {
        await CleanupAsync(default).ConfigureAwait(false);

        return users.GetUsers(context.DocumentName);
    }
}
