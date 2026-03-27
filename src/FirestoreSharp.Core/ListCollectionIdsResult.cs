namespace FirestoreSharp.Core;

public sealed class ListCollectionIdsResult(IReadOnlyList<string> collectionIds, string? nextPageToken)
{
    public IReadOnlyList<string> CollectionIds { get; } = collectionIds;
    public string? NextPageToken { get; } = nextPageToken;
}
