using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;

namespace FirestoreSharp.Core;

public interface IDocumentService
{
    Task<Document> CreateAsync(DocumentPath path, Document document, CancellationToken cancellationToken = default);
    Task<Document> GetAsync(DocumentPath path, CancellationToken cancellationToken = default);
    IAsyncEnumerable<BatchGetResult> BatchGetAsync(IReadOnlyList<string> resourceNames, CancellationToken cancellationToken = default);
    Task<ListDocumentsResult> ListAsync(string parent, string collectionId, int pageSize, string? pageToken, CancellationToken cancellationToken = default);
    Task<Document> UpdateAsync(DocumentPath path, Document document, IReadOnlyList<string>? updateMaskFieldPaths, CancellationToken cancellationToken = default);
    Task DeleteAsync(DocumentPath path, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Document>> RunQueryAsync(string parent, StructuredQuery query, CancellationToken cancellationToken = default);
    Task<WriteResult> ExecuteWriteAsync(Write write, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WriteResult>> CommitAsync(IReadOnlyList<Write> writes, IReadOnlyDictionary<string, Timestamp?>? transactionReadSet, CancellationToken cancellationToken = default);
    Task<ListCollectionIdsResult> ListCollectionIdsAsync(string parent, int pageSize, string? pageToken, CancellationToken cancellationToken = default);
    Task<AggregationQueryResult> RunAggregationQueryAsync(string parent, StructuredAggregationQuery aggregationQuery, CancellationToken cancellationToken = default);
    Task<PartitionQueryResult> PartitionQueryAsync(string parent, StructuredQuery query, long partitionCount, int pageSize, string? pageToken, CancellationToken cancellationToken = default);
}
