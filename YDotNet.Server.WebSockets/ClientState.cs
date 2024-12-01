using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security;

namespace YDotNet.Server.WebSockets;

public record SubDocumentContext(string DocId, Queue<byte[]> PendingUpdates, bool IsSynced);

public sealed class ClientState : IDisposable
{
    private readonly SemaphoreSlim slimLock = new(1);

    required public WebSocket WebSocket { get; set; }

    required public WebSocketEncoder Encoder { get; set; }

    required public WebSocketDecoder Decoder { get; set; }

    required public DocumentContext DocumentContext { get; set; }

    public string DocumentName => this.DocumentContext.DocumentName;

    public IReadOnlyDictionary<string, SubDocumentContext> SubDocuments => _subDocuments;
    private readonly ConcurrentDictionary<string,SubDocumentContext>  _subDocuments = new(StringComparer.Ordinal);
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
        
         var subDocument = AddSubDocument(docId);
         subDocument.PendingUpdates.Enqueue(update);
    }
    public SubDocumentContext AddSubDocument(string docId)
    {
        return _subDocuments.AddOrUpdate(docId,
            id=>new SubDocumentContext(id,new Queue<byte[]>(),false),
            (id,ctx) => ctx);
    }
    
    public bool TryGetSubDocument(string docId, out SubDocumentContext? subDoc)
    {
        return _subDocuments.TryGetValue(docId, out subDoc);
    }

    public bool TryValidateDocument(string docId)
    {
        if (DocumentName == docId) return true;
        if (SubDocuments.ContainsKey(docId)) return true;
        return false;
    }
    
    public void ValidateDocument(string docId)
    {
       if (!TryValidateDocument(docId))
           throw new InvalidOperationException($"Invalid document id: {docId}, it is neither state root doc neither a subdocument of {DocumentName}");
    }
    
    public void Dispose()
    {
        WebSocket.Dispose();

        Encoder.Dispose();
        Decoder.Dispose();
    }
}
