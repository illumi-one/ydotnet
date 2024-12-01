using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace YDotNet.Server.WebSockets;

public record PendingUpdate(string DocId, byte[] Update);

public record SubDocumentContext(string DocId, Queue<byte[]> PendingUpdates, bool IsSynced);

public sealed class ClientState : IDisposable
{
    private readonly SemaphoreSlim slimLock = new(1);

    required public WebSocket WebSocket { get; set; }

    required public WebSocketEncoder Encoder { get; set; }

    required public WebSocketDecoder Decoder { get; set; }

    required public DocumentContext DocumentContext { get; set; }

    public string DocumentName => this.DocumentContext.DocumentName;
    
    public readonly ConcurrentDictionary<string,SubDocumentContext>  SubDocuments= new ();
    public bool IsSynced { get; set; }
    public Queue<byte[]> PendingUpdates { get; } = new();
    
    public async Task WriteLockedAsync<T>(T state, Func<WebSocketEncoder, T, ClientState, CancellationToken, Task> action, CancellationToken ct)
    {
        await slimLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await action(Encoder, state, this, ct).ConfigureAwait(false);
        }
        finally
        {
            slimLock.Release();
        }
    }

    public void AddPendingUpdate(string docId, byte[] update)
    {
        if (docId == DocumentName)
        {
            PendingUpdates.Enqueue(update);
            return;
        }
        
         var subDocument = GetSubDocument(docId);
         subDocument.PendingUpdates.Enqueue(update);
    }
    public SubDocumentContext GetSubDocument(string docId)
    {
        return SubDocuments.AddOrUpdate(docId,
            id=>new SubDocumentContext(id,new Queue<byte[]>(),false),
            (id,ctx) => ctx);
    }

    public void Dispose()
    {
        WebSocket.Dispose();

        Encoder.Dispose();
        Decoder.Dispose();
    }
}
