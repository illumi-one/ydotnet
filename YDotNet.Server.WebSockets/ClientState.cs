using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security;
using Microsoft.Extensions.Logging;

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
        if( docId == DocumentName )
        {
            PendingUpdates.Enqueue(update);
        }
        else
        {
            if (!_subDocuments.TryGetValue(docId, out var subDoc))
            {
                throw new InvalidOperationException($"Subdocument not found: {docId}");
            }

            subDoc.PendingUpdates.Enqueue(update);
        }
    }

    public DocumentContext GetOrInitDocument(string docId)
    {
       // ValidateDocument(docId);
        if (docId == DocumentName)
            return DocumentContext;
        
        if (_subDocuments.TryGetValue(docId, out var subDoc))
            return subDoc.Context;

        var newSubDoc =   _subDocuments.AddOrUpdate(docId,id=>
                new SubDocumentContext(DocumentContext with { DocumentName = docId }, new Queue<byte[]>(), false),
                (id,ctx) => ctx);
        return newSubDoc.Context;
    }
    
    

    private bool IsExisting(string docId)
    {
        if (DocumentName == docId) return true;
        if (SubDocuments.ContainsKey(docId)) return true;
        return false;
    }

    public void EnsureExists(string docId)
    {
       if (!IsExisting(docId))
           throw new InvalidOperationException($"Invalid document id: {docId}, it is neither state root doc neither a subdocument of {DocumentName}");
    }
    
    public void Dispose()
    {
        WebSocket.Dispose();

        Encoder.Dispose();
        Decoder.Dispose();
    }

    public void EnchanceWithClientId(string docId, ulong clientId)
    {
        if(docId==DocumentName)
        {
            DocumentContext = DocumentContext with { ClientId = clientId };
            return;
        }
        if (_subDocuments.TryGetValue(docId, out var subDoc))
        {
            _subDocuments.TryUpdate(subDoc.Context.DocumentName,
                subDoc with { Context = subDoc.Context with { ClientId = clientId } },
                subDoc);
        }
    }
}
