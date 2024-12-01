using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using YDotNet.Document;
using YDotNet.Document.Transactions;
using YDotNet.Server.Storage;

namespace YDotNet.Server.Internal;

public static class YDocExtentions
{
    public static string CalculateHashSum(this byte[]? data)
    {
        if(data == null) return string.Empty;
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hashBytes = sha256.ComputeHash(data);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }
    public static string SnapshotStart(this Transaction transaction)
    {
        return Convert.ToBase64String(transaction.Snapshot()).Substring(0,20);
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

        delayedWriter = new DelayedWriter(options.StoreDebounce, options.MaxWriteTimeInterval, WriteAsync);

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
        logger.LogDebug("Loaded  document {name} with size {size}, hash {hash}", documentName,documentData?.Length, documentData.CalculateHashSum());
        if (documentData != null)
        {
            var document = new Doc();

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
            return new Doc();
        }

        throw new InvalidOperationException("Document does not exist yet.");
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
            logger.LogDebug("before applying update to document data.");
            var t = await action(document).ConfigureAwait(false);
            logger.LogDebug("after applying update to document data.");
            return t;
        }
        finally
        {
            slimLock.Release();
        }
    }

    private async Task WriteAsync()
    {
        var document = await loadingTask.ConfigureAwait(false);

        logger.LogDebug("Document {documentName} will be saved.", documentName);
        try
        {
            // All the writes are thread safe itself, but they have to be synchronized with a write.
            var state = GetStateLocked(document);

            await documentStorage.StoreDocAsync(documentName, state).ConfigureAwait(false);

            logger.LogDebug("Document {documentName} with size {size} hash {hash} been saved.", documentName, state.Length, state.CalculateHashSum());
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
