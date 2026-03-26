using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core;

public interface IDocumentStore
{
    Task CreateAsync(Document document, CancellationToken cancellationToken = default);

    Task<Document> GetAsync(string name, CancellationToken cancellationToken = default);
}
