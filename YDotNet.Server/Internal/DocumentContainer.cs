using Microsoft.Extensions.Logging;
using YDotNet.Document;
using YDotNet.Server.Storage;

namespace YDotNet.Server.Internal;

internal sealed class DocumentContainer
{
    private readonly IDocumentStorage documentStorage;
    private readonly DocumentManagerOptions options;
    private readonly ILogger logger;
    private readonly string documentName;
    private readonly Task<Doc> loadingTask;
    private readonly SemaphoreSlim slimLock = new(1);
    private readonly DelayedWriter writer;
    private Doc? doc;

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

        writer = new DelayedWriter(options.StoreDebounce, options.MaxWriteTimeInterval, WriteAsync);

        loadingTask = LoadInternalAsync(documentCallback, documentManager);
    }

    public Task FlushAsync()
    {
        return writer.FlushAsync();
    }

    private async Task<Doc> LoadInternalAsync(IDocumentCallback documentCallback, IDocumentManager documentManager)
    {
        doc = await LoadCoreAsync().ConfigureAwait(false);

        await documentCallback.OnDocumentLoadedAsync(new DocumentLoadEvent
        {
            Document = doc,
            Context = new DocumentContext(documentName, 0),
            Source = documentManager,
        }).ConfigureAwait(false);

        doc.ObserveUpdatesV1(e =>
        {
            writer.Ping();
        });

        return doc;
    }

    private async Task<Doc> LoadCoreAsync()
    {
        var documentData = await documentStorage.GetDocAsync(documentName).ConfigureAwait(false);

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

    public async Task<T> ApplyUpdateReturnAsync<T>(Func<Doc, T> action)
    {
        var document = await loadingTask.ConfigureAwait(false);

        await slimLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return action(document);
        }
        finally
        {
            slimLock.Release();
        }
    }

    private async Task WriteAsync()
    {
        var curentDoc = doc;

        if (curentDoc == null)
        {
            return;
        }

        byte[] state;

        logger.LogDebug("Document {documentName} will be written to stroage {storage}", documentName, documentStorage);
        try
        {
            await slimLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using var transaction = curentDoc.ReadTransactionOrThrow();

                var snapshot = transaction!.Snapshot()!;

                state = transaction.StateDiffV1(snapshot)!;
            }
            finally
            {
                slimLock.Release();
            }

            await documentStorage.StoreDocAsync(documentName, state).ConfigureAwait(false);

            logger.LogDebug("Document {documentName} has been written to storage {storage}", documentName, documentStorage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Document {documentName} failed to write to storage {storage}", documentName, documentStorage);
        }
    }
}
