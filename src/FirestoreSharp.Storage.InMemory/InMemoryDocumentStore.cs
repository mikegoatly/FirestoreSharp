using System.Collections.Concurrent;
using FirestoreSharp.Core;
using Google.Cloud.Firestore.V1;
using Grpc.Core;

namespace FirestoreSharp.Storage.InMemory;

public sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly ConcurrentDictionary<string, Document> _documents = new();

    public Task CreateAsync(FirestorePath path, Document document, CancellationToken cancellationToken = default)
    {
        if (!_documents.TryAdd(path.ResourceName, document.Clone()))
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, $"Document already exists: {path.ResourceName}"));
        }

        return Task.CompletedTask;
    }

    public Task<Document> GetAsync(FirestorePath path, CancellationToken cancellationToken = default)
    {
        if (!_documents.TryGetValue(path.ResourceName, out var document))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Document not found: {path.ResourceName}"));
        }

        return Task.FromResult(document.Clone());
    }

    public Task<Document> UpdateAsync(FirestorePath path, Document document, CancellationToken cancellationToken = default)
    {
        if (!_documents.TryGetValue(path.ResourceName, out var existing))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Document not found: {path.ResourceName}"));
        }

        _documents[path.ResourceName] = document.Clone();

        return Task.FromResult(document.Clone());
    }

    public Task DeleteAsync(FirestorePath path, CancellationToken cancellationToken = default)
    {
        if (!_documents.TryRemove(path.ResourceName, out _))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Document not found: {path.ResourceName}"));
        }

        return Task.CompletedTask;
    }
}
