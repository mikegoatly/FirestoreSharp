using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core;

public sealed class ListDocumentsResult(IReadOnlyList<Document> documents, string? nextPageToken)
{
    public IReadOnlyList<Document> Documents { get; } = documents;
    public string? NextPageToken { get; } = nextPageToken;
}
