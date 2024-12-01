using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security;

namespace YDotNet.Server.WebSockets;

public record SubDocumentContext(DocumentContext Context, Queue<byte[]> PendingUpdates, bool IsSynced);

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
    
    public async Task WriteLockedAsync<T>(T message, Func<WebSocketEncoder, T, ClientState, CancellationToken, Task> action, CancellationToken ct)
    {
        await slimLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await action(Encoder, message, this, ct).ConfigureAwait(false);
        }
        finally
        {
            slimLock.Release();
        }
    }

    public void AddPendingUpdate(string docId, byte[] update)
    {
        if(docId == DocumentName)
        {
            PendingUpdates.Enqueue(update);
        }
        else
        {
            var doc = GetSubDocument(docId);
            doc.PendingUpdates.Enqueue(update);
        }
    }
    
    public SubDocumentContext AddSubDocument(DocumentContext context)
    {
        return _subDocuments.AddOrUpdate(context.DocumentName,
            id=>new SubDocumentContext(context,new Queue<byte[]>(),false),
            (id,ctx) => ctx);
    }
    
    private bool TryGetSubDocument(string docId, out SubDocumentContext? subDoc)
    {
        return _subDocuments.TryGetValue(docId, out subDoc);
    }

    public bool TryValidateDocument(string docId)
    {
        if (DocumentName == docId) return true;
        if (SubDocuments.ContainsKey(docId)) return true;
        return false;
    }

    public SubDocumentContext GetSubDocument(string docId)
    {
        if (!TryGetSubDocument(docId, out var subDoc))
            throw new InvalidOperationException($"Subdocument not found: {docId}");
        return subDoc;
    }
    
    public DocumentContext GetDocument(string docId)
    {
        ValidateDocument(docId);
        if (docId == DocumentName)
            return DocumentContext;
        
        if (!TryGetSubDocument(docId, out var subDoc))
            throw new InvalidOperationException($"Subdocument not found: {docId}");
        
        return subDoc.Context;
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
