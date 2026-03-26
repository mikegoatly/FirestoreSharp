using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core;

public interface IDocumentService
{
    Task<Document> CreateAsync(FirestorePath path, Document document, CancellationToken cancellationToken = default);
    Task<Document> GetAsync(FirestorePath path, CancellationToken cancellationToken = default);
    IAsyncEnumerable<BatchGetResult> BatchGetAsync(IReadOnlyList<string> resourceNames, CancellationToken cancellationToken = default);
    Task<ListDocumentsResult> ListAsync(string parent, string collectionId, int pageSize, string? pageToken, CancellationToken cancellationToken = default);
    Task<Document> UpdateAsync(FirestorePath path, Document document, IReadOnlyList<string>? updateMaskFieldPaths, CancellationToken cancellationToken = default);
    Task DeleteAsync(FirestorePath path, CancellationToken cancellationToken = default);
}
