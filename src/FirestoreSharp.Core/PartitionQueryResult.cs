using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core;

public sealed class PartitionQueryResult(IReadOnlyList<Cursor> partitions, string? nextPageToken)
{
    public IReadOnlyList<Cursor> Partitions { get; } = partitions;
    public string? NextPageToken { get; } = nextPageToken;
}
