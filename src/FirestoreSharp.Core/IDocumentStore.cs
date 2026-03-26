using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core;

public interface IDocumentStore
{
    Task CreateAsync(FirestorePath path, Document document, CancellationToken cancellationToken = default);

    Task<Document> GetAsync(FirestorePath path, CancellationToken cancellationToken = default);
}
