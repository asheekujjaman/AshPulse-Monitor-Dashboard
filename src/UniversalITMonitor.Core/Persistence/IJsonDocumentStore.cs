namespace UniversalITMonitor.Core.Persistence;

public interface IJsonDocumentStore
{
    Task<TDocument> ReadAsync<TDocument>(
        string fileName,
        Func<TDocument> defaultFactory,
        CancellationToken cancellationToken = default);

    Task EnsureAsync<TDocument>(
        string fileName,
        TDocument defaultDocument,
        CancellationToken cancellationToken = default);

    Task WriteAsync<TDocument>(
        string fileName,
        TDocument document,
        CancellationToken cancellationToken = default);

    Task<TResult> UpdateAsync<TDocument, TResult>(
        string fileName,
        Func<TDocument> defaultFactory,
        Func<TDocument, (TDocument Document, TResult Result)> update,
        CancellationToken cancellationToken = default);
}
