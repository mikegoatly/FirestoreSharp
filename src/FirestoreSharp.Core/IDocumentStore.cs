using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core;

public interface IDocumentStore
{
    Task CreateAsync(DocumentPath path, Document document, CancellationToken cancellationToken = default);

    Task<Document> GetAsync(DocumentPath path, CancellationToken cancellationToken = default);

    Task<Document?> TryGetAsync(DocumentPath path, CancellationToken cancellationToken = default);

    IAsyncEnumerable<Document> ListAsync(ReadOnlyMemory<char> parentPrefix, CancellationToken cancellationToken = default);

    Task<Document> UpdateAsync(DocumentPath path, Document document, CancellationToken cancellationToken = default);

    Task DeleteAsync(DocumentPath path, CancellationToken cancellationToken = default);

    /// <summary>Returns all distinct (project, database) pairs that have stored documents.</summary>
    Task<IReadOnlyList<(string Project, string Database)>> GetKnownDatabasesAsync(CancellationToken cancellationToken = default);
}
