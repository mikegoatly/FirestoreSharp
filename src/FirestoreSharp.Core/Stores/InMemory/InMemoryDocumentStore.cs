using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using FirestoreSharp.Core;

using Google.Cloud.Firestore.V1;

using Grpc.Core;

namespace FirestoreSharp.Core.Stores.InMemory;

internal sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly ConcurrentDictionary<string, Document> _documents = new();

    public Task CreateAsync(DocumentPath path, Document document, CancellationToken cancellationToken = default)
    {
        if (!_documents.TryAdd(path.ResourceName, document.Clone()))
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, $"Document already exists: {path.ResourceName}"));
        }

        return Task.CompletedTask;
    }

    public Task<Document> GetAsync(DocumentPath path, CancellationToken cancellationToken = default)
    {
        if (!_documents.TryGetValue(path.ResourceName, out var document))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Document not found: {path.ResourceName}"));
        }

        return Task.FromResult(document.Clone());
    }

    public Task<Document?> TryGetAsync(DocumentPath path, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_documents.TryGetValue(path.ResourceName, out var document) ? document.Clone() : null);
    }

    public async IAsyncEnumerable<Document> ListAsync(ReadOnlyMemory<char> parentPrefix, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var documents = _documents
            .Where(kvp => kvp.Key.AsSpan().StartsWith(parentPrefix.Span, StringComparison.Ordinal))
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal);

        foreach (var kvp in documents)
        {
            yield return kvp.Value.Clone();
        }

    }

    public Task<Document> UpdateAsync(DocumentPath path, Document document, CancellationToken cancellationToken = default)
    {
        if (!_documents.TryGetValue(path.ResourceName, out var existing))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Document not found: {path.ResourceName}"));
        }

        _documents[path.ResourceName] = document.Clone();

        return Task.FromResult(document.Clone());
    }

    public Task DeleteAsync(DocumentPath path, CancellationToken cancellationToken = default)
    {
        if (!_documents.TryRemove(path.ResourceName, out _))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Document not found: {path.ResourceName}"));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string Project, string Database)>> GetKnownDatabasesAsync(CancellationToken cancellationToken = default)
    {
        var results = _documents.Keys
            .Select(key =>
            {
                // Format: projects/{project}/databases/{database}/documents/...
                var parts = key.Split('/');
                return parts.Length >= 4 ? (parts[1], parts[3]) : default;
            })
            .Where(pair => pair != default)
            .Distinct()
            .OrderBy(pair => pair.Item1).ThenBy(pair => pair.Item2)
            .ToList();

        return Task.FromResult<IReadOnlyList<(string Project, string Database)>>(results);
    }
}
