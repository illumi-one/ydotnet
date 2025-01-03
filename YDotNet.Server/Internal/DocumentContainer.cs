using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using YDotNet.Document;
using YDotNet.Document.Options;
using YDotNet.Document.Transactions;
using YDotNet.Server.Storage;

namespace YDotNet.Server.Internal;

public static class YDocExtentions
{
    public static string GetHash(this byte[]? data)
    {
        if (data == null)
        {
            return string.Empty;
        }

        using SHA256 sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(data);
        return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
    }

    public static string GetBase64Part(this byte[]? data)
    {
        if (data == null)
        {
            return string.Empty;
        }

        var base64String = Convert.ToBase64String(data);
        return base64String.Length > 20 ? base64String.Substring(0,20) : base64String;
    }
    
    public static string GetSnapshotHash(this Transaction transaction)
    {
        return transaction.Snapshot().GetHash();
    }
    
    public static string SnapshotStart(this Transaction transaction)
    {
        return transaction.Snapshot().GetBase64Part();
    }
}
internal sealed class DocumentContainer
{
    private readonly DocumentManagerOptions options;
    private readonly ILogger logger;
    private readonly DelayedWriter delayedWriter;
    private readonly string documentName;
    private readonly IDocumentStorage documentStorage;
    private readonly Task<Doc> loadingTask;
    private readonly SemaphoreSlim slimLock = new(1);

    public string Name => documentName;

    public DocumentContainer(
        string documentName,
        IDocumentStorage documentStorage,
        IDocumentCallback documentCallback,
        IDocumentManager documentManager,
        DocumentManagerOptions options,
        ILogger logger)
    {
        this.documentName = documentName;
        this.documentStorage = documentStorage;
        this.options = options;
        this.logger = logger;

        delayedWriter = new DelayedWriter(options.StoreDebounce, options.MaxWriteTimeInterval, () => WriteAsync(documentCallback, documentManager));

        loadingTask = LoadInternalAsync(documentCallback, documentManager, logger);
    }

    private async Task<Doc> LoadInternalAsync(IDocumentCallback documentCallback, IDocumentManager documentManager, ILogger logger)
    {
        var doc = await LoadCoreAsync().ConfigureAwait(false);

        await documentCallback.OnDocumentLoadedAsync(new DocumentLoadEvent
        {
            Document = doc,
            Context = new DocumentContext(documentName, 0),
            Source = documentManager,
        }).ConfigureAwait(false);

        doc.ObserveUpdatesV1(e =>
        {
            logger.LogDebug("Document {name} updated.", documentName);

            delayedWriter.Ping();
        });

        return doc;
    }

    private async Task<Doc> LoadCoreAsync()
    {
        var documentData = await documentStorage.GetDocAsync(documentName).ConfigureAwait(false);
        logger.LogDebug("Loaded  document {name} with size {size}, hash {hash}", documentName,documentData?.Length, documentData.GetBase64Part());
        if (documentData != null)
        {
            var document = CreateNewDoc();

            using (var transaction = document.WriteTransaction())
            {
                if (transaction == null)
                {
                    throw new InvalidOperationException("Transaction cannot be acquired.");
                }
                transaction.ApplyV1(documentData);
            }

            return document;
        }

        if (options.AutoCreateDocument)
        {
            return CreateNewDoc();
        }

        throw new InvalidOperationException("Document does not exist yet.");
    }

    private Doc CreateNewDoc()
    {
        return new Doc(new DocOptions { SkipGarbageCollection = !options.EnableGCinNewDocs });
    }

    public async Task DisposeAsync()
    {
        await delayedWriter.FlushAsync().ConfigureAwait(false);
    }

    public async Task<T> ApplyUpdateReturnAsync<T>(Func<Doc, Task<T>> action)
    {
        var document = await loadingTask.ConfigureAwait(false);

        // This is the only option to get access to the document to prevent concurrency issues.
        await slimLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await action(document).ConfigureAwait(false);
        }
        finally
        {
            slimLock.Release();
        }
    }

    private async Task WriteAsync(IDocumentCallback callback, IDocumentManager manager)
    {
        var document = await loadingTask.ConfigureAwait(false);

        logger.LogDebug("Document {documentName} will be saved.", documentName);
        try
        {
            // All the writes are thread safe itself, but they have to be synchronized with a write.
            var state = GetStateLocked(document);

            await documentStorage.StoreDocAsync(documentName, state).ConfigureAwait(false);
            logger.LogDebug("Document {documentName} with size {size} hash {hash} been saved.", documentName, state.Length, state.GetBase64Part());

            await callback.OnDocumentSavedAsync(new DocumentSavedEvent
            {
                Document = document,
                Context = new DocumentContext(documentName, 0),
                Source = manager,
                State = state
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Document {documentName} could not be saved.", documentName);
        }
    }

    private byte[] GetStateLocked(Doc document)
    {
        slimLock.Wait();
        try
        {
            using var transaction = document.ReadTransaction();

            return transaction.StateDiffV1(stateVector: null)!;
        }
        finally
        {
            slimLock.Release();
        }
    }
}
