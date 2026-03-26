using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core;

public interface IDocumentStore
{
    Task CreateAsync(FirestorePath path, Document document, CancellationToken cancellationToken = default);

    Task<Document> GetAsync(FirestorePath path, CancellationToken cancellationToken = default);

    Task<Document?> TryGetAsync(FirestorePath path, CancellationToken cancellationToken = default);

    IAsyncEnumerable<Document> ListAsync(string parentPrefix, CancellationToken cancellationToken = default);

    Task<Document> UpdateAsync(FirestorePath path, Document document, CancellationToken cancellationToken = default);

    Task DeleteAsync(FirestorePath path, CancellationToken cancellationToken = default);
}
