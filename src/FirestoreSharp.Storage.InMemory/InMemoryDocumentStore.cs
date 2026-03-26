using System.Collections.Concurrent;
using Google.Cloud.Firestore.V1;
using Grpc.Core;

namespace FirestoreSharp.Storage.InMemory;

public sealed class InMemoryDocumentStore : Core.IDocumentStore
{
    private readonly ConcurrentDictionary<string, Document> _documents = new();

    public Task CreateAsync(Document document, CancellationToken cancellationToken = default)
    {
        if (!_documents.TryAdd(document.Name, document.Clone()))
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, $"Document already exists: {document.Name}"));
        }

        return Task.CompletedTask;
    }

    public Task<Document> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_documents.TryGetValue(name, out var document))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Document not found: {name}"));
        }

        return Task.FromResult(document.Clone());
    }
}
