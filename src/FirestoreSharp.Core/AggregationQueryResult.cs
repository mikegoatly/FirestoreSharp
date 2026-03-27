using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core;

public sealed class AggregationQueryResult(AggregationResult aggregationResult, IReadOnlyList<Document> sourceDocuments)
{
    public AggregationResult AggregationResult { get; } = aggregationResult;

    /// <summary>
    /// The documents that were scanned to produce the aggregation result.
    /// Used by callers to record individual document reads against a transaction's read-set.
    /// </summary>
    public IReadOnlyList<Document> SourceDocuments { get; } = sourceDocuments;
}
