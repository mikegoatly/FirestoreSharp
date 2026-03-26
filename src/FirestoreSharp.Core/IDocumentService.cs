using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core;

public interface IDocumentService
{
    Task<Document> CreateAsync(FirestorePath path, Document document, CancellationToken cancellationToken = default);
    Task<Document> GetAsync(FirestorePath path, CancellationToken cancellationToken = default);
    Task<Document> UpdateAsync(FirestorePath path, Document document, IReadOnlyList<string>? updateMaskFieldPaths, CancellationToken cancellationToken = default);
    Task DeleteAsync(FirestorePath path, CancellationToken cancellationToken = default);
}
