using FirestoreSharp.Core.Listeners;
using FirestoreSharp.Core.Query;

using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

namespace FirestoreSharp.Core;

internal sealed class DocumentService(IDocumentStore store, IDocumentChangeNotifier changeNotifier) : IDocumentService, IDisposable
{
    private readonly SemaphoreSlim _commitLock = new(1, 1);

    public void Dispose() => _commitLock.Dispose();
    public async Task<Document> CreateAsync(DocumentPath path, Document document, CancellationToken cancellationToken = default)
    {
        var now = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        var created = document.Clone();
        created.Name = path.ResourceName;
        created.CreateTime = now;
        created.UpdateTime = now;

        await store.CreateAsync(path, created, cancellationToken).ConfigureAwait(false);

        changeNotifier.NotifyDocumentsChanged([new DocumentMutation(created.Name, created)]);

        return created;
    }

    public async Task<Document> GetAsync(DocumentPath path, CancellationToken cancellationToken = default)
    {
        return await store.GetAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<BatchGetResult> BatchGetAsync(IReadOnlyList<string> resourceNames, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var resourceName in resourceNames)
        {
            var path = DocumentPath.Parse(resourceName);
            var readTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
            var document = await store.TryGetAsync(path, cancellationToken).ConfigureAwait(false);

            yield return document is not null
                ? new BatchGetFoundResult(document, readTime)
                : new BatchGetMissingResult(resourceName, readTime);
        }
    }

    public async Task<ListDocumentsResult> ListAsync(string parent, string collectionId, int pageSize, string? pageToken, CancellationToken cancellationToken = default)
    {
        const int defaultPageSize = 100;
        var effectivePageSize = pageSize > 0 ? pageSize : defaultPageSize;

        var parentPrefix = string.IsNullOrEmpty(collectionId)
            ? parent
            : $"{parent}/{collectionId}";

        var documents = new List<Document>();
        string? nextPageToken = null;

        await foreach (var document in store.ListAsync(parentPrefix, cancellationToken).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(pageToken) && string.Compare(document.Name, pageToken, StringComparison.Ordinal) <= 0)
            {
                continue;
            }

            if (documents.Count >= effectivePageSize)
            {
                nextPageToken = documents[^1].Name;
                break;
            }

            documents.Add(document);
        }

        return new ListDocumentsResult(documents, nextPageToken);
    }

    public async Task<Document> UpdateAsync(DocumentPath path, Document document, IReadOnlyList<string>? updateMaskFieldPaths, CancellationToken cancellationToken = default)
    {
        var existing = await store.GetAsync(path, cancellationToken).ConfigureAwait(false);

        var updated = existing.Clone();
        updated.UpdateTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        if (updateMaskFieldPaths is { Count: > 0 })
        {
            foreach (var rawPath in updateMaskFieldPaths)
            {
                var fieldPath = FieldPath.Parse(rawPath);
                var sourceValue = DocumentNavigator.GetValue(document, fieldPath);

                if (sourceValue is not null)
                {
                    DocumentNavigator.SetValue(updated, fieldPath, sourceValue);
                }
                else
                {
                    DocumentNavigator.RemoveValue(updated, fieldPath);
                }
            }
        }
        else
        {
            updated.Fields.Clear();
            updated.Fields.Add(document.Fields);
        }

        var result = await store.UpdateAsync(path, updated, cancellationToken).ConfigureAwait(false);

        changeNotifier.NotifyDocumentsChanged([new DocumentMutation(result.Name, result)]);

        return result;
    }

    public async Task DeleteAsync(DocumentPath path, CancellationToken cancellationToken = default)
    {
        await store.DeleteAsync(path, cancellationToken).ConfigureAwait(false);

        changeNotifier.NotifyDocumentsChanged([new DocumentMutation(path.ResourceName, null)]);
    }

    public async Task<ListCollectionIdsResult> ListCollectionIdsAsync(string parent, int pageSize, string? pageToken, CancellationToken cancellationToken = default)
    {
        const int defaultPageSize = 300;
        var effectivePageSize = pageSize > 0 ? pageSize : defaultPageSize;

        // Documents live at "{parent}/{collectionId}/{documentId}/..." so we need
        // the prefix "{parent}/" to find all descendants, then extract the immediate
        // child collection segment.
        var parentPrefix = parent.EndsWith('/') ? parent : $"{parent}/";

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var collectionIds = new List<string>();
        string? nextPageToken = null;

        await foreach (var document in store.ListAsync(parentPrefix, cancellationToken).ConfigureAwait(false))
        {
            // Strip the parent prefix to get "collectionId/docId[/...]"
            var remainder = document.Name.AsSpan()[parentPrefix.Length..];
            var slash = remainder.IndexOf('/');

            // Each document must have at least one slash (collectionId/docId)
            if (slash <= 0)
            {
                continue;
            }

            var collectionId = remainder[..slash].ToString();

            // Page token is the last collection ID returned on the previous page;
            // skip until we pass it (ordinal ordering matches store ordering).
            if (!string.IsNullOrEmpty(pageToken)
                && string.Compare(collectionId, pageToken, StringComparison.Ordinal) <= 0)
            {
                continue;
            }

            if (seen.Add(collectionId))
            {
                if (collectionIds.Count >= effectivePageSize)
                {
                    nextPageToken = collectionIds[^1];
                    break;
                }

                collectionIds.Add(collectionId);
            }
        }

        return new ListCollectionIdsResult(collectionIds, nextPageToken);
    }

    public async Task<IReadOnlyList<Document>> RunQueryAsync(string parent, StructuredQuery query, CancellationToken cancellationToken = default)
    {
        var candidates = new List<Document>();
        await foreach (var document in store.ListAsync(parent, cancellationToken).ConfigureAwait(false))
        {
            candidates.Add(document);
        }

        return QueryEngine.Execute(parent, query, candidates);
    }

    public async Task<AggregationQueryResult> RunAggregationQueryAsync(string parent, StructuredAggregationQuery aggregationQuery, CancellationToken cancellationToken = default)
    {
        const int minAggregations = 1;
        const int maxAggregations = 5;

        if (aggregationQuery.Aggregations.Count < minAggregations || aggregationQuery.Aggregations.Count > maxAggregations)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"A minimum of {minAggregations} and maximum of {maxAggregations} aggregations per query are allowed."));
        }

        var innerQuery = aggregationQuery.QueryTypeCase == StructuredAggregationQuery.QueryTypeOneofCase.StructuredQuery
            ? aggregationQuery.StructuredQuery
            : new StructuredQuery();

        var candidates = new List<Document>();
        await foreach (var document in store.ListAsync(parent, cancellationToken).ConfigureAwait(false))
        {
            candidates.Add(document);
        }

        var documents = QueryEngine.Execute(parent, innerQuery, candidates);
        var aggregationResult = AggregationEngine.Execute(aggregationQuery, documents);
        return new AggregationQueryResult(aggregationResult, documents);
    }

    public async Task<WriteResult> ExecuteWriteAsync(Write write, CancellationToken cancellationToken = default)
    {
        var now = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        switch (write.OperationCase)
        {
            case Write.OperationOneofCase.Update:
                {
                    var path = DocumentPath.Parse(write.Update.Name);
                    var existing = await store.TryGetAsync(path, cancellationToken).ConfigureAwait(false);

                    CheckPrecondition(existing, write.CurrentDocument, path.ResourceName);

                    Document updated;
                    var maskPaths = write.UpdateMask?.FieldPaths;

                    if (existing is null)
                    {
                        updated = write.Update.Clone();
                        updated.Name = path.ResourceName;
                        updated.CreateTime = now;
                        updated.UpdateTime = now;

                        if (maskPaths is { Count: > 0 })
                        {
                            // Create with only the masked fields present
                            updated.Fields.Clear();
                            foreach (var rawPath in maskPaths)
                            {
                                var fieldPath = FieldPath.Parse(rawPath);
                                var val = DocumentNavigator.GetValue(write.Update, fieldPath);
                                if (val is not null)
                                {
                                    DocumentNavigator.SetValue(updated, fieldPath, val);
                                }
                            }
                        }

                        await store.CreateAsync(path, updated, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        updated = existing.Clone();
                        updated.UpdateTime = now;

                        if (maskPaths is { Count: > 0 })
                        {
                            foreach (var rawPath in maskPaths)
                            {
                                var fieldPath = FieldPath.Parse(rawPath);
                                var val = DocumentNavigator.GetValue(write.Update, fieldPath);
                                if (val is not null)
                                {
                                    DocumentNavigator.SetValue(updated, fieldPath, val);
                                }
                                else
                                {
                                    DocumentNavigator.RemoveValue(updated, fieldPath);
                                }
                            }
                        }
                        else
                        {
                            updated.Fields.Clear();
                            updated.Fields.Add(write.Update.Fields);
                        }

                        await store.UpdateAsync(path, updated, cancellationToken).ConfigureAwait(false);
                    }

                    var writeResult = new WriteResult { UpdateTime = now };
                    changeNotifier.NotifyDocumentsChanged([new DocumentMutation(updated.Name, updated)]);
                    return writeResult;
                }

            case Write.OperationOneofCase.Delete:
                {
                    var path = DocumentPath.Parse(write.Delete);
                    var existing = await store.TryGetAsync(path, cancellationToken).ConfigureAwait(false);

                    CheckPrecondition(existing, write.CurrentDocument, path.ResourceName);

                    if (existing is not null)
                    {
                        await store.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
                        changeNotifier.NotifyDocumentsChanged([new DocumentMutation(path.ResourceName, null)]);
                    }

                    return new WriteResult(); // no update_time after a delete
                }

            case Write.OperationOneofCase.Transform:
                throw new RpcException(new Status(StatusCode.Unimplemented, "Transform writes are not supported."));

            default:
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Write must have an operation set."));
        }
    }

    private static void CheckPrecondition(Document? existing, Precondition? precondition, string resourceName)
    {
        if (precondition is null or { ConditionTypeCase: Precondition.ConditionTypeOneofCase.None })
        {
            return;
        }

        switch (precondition.ConditionTypeCase)
        {
            case Precondition.ConditionTypeOneofCase.Exists when precondition.Exists:
                if (existing is null)
                {
                    throw new RpcException(new Status(StatusCode.FailedPrecondition,
                        $"Document '{resourceName}' does not exist."));
                }

                break;

            case Precondition.ConditionTypeOneofCase.Exists:
                if (existing is not null)
                {
                    throw new RpcException(new Status(StatusCode.FailedPrecondition,
                        $"Document '{resourceName}' already exists."));
                }

                break;

            case Precondition.ConditionTypeOneofCase.UpdateTime:
                if (existing is null)
                {
                    throw new RpcException(new Status(StatusCode.FailedPrecondition,
                        $"Document '{resourceName}' does not exist."));
                }

                if (!existing.UpdateTime.Equals(precondition.UpdateTime))
                {
                    throw new RpcException(new Status(StatusCode.FailedPrecondition,
                        $"Document '{resourceName}' update time does not match precondition."));
                }

                break;
        }
    }

    // ── Atomic Commit (prepare-then-apply) ────────────────────────────────────

    public async Task<IReadOnlyList<WriteResult>> CommitAsync(
        IReadOnlyList<Write> writes,
        IReadOnlyDictionary<string, Timestamp?>? transactionReadSet,
        CancellationToken cancellationToken = default)
    {
        const int maxWritesPerTransaction = 500;
        if (writes.Count > maxWritesPerTransaction)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"A transaction cannot contain more than {maxWritesPerTransaction} writes."));
        }

        await _commitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 1. Validate transaction read-set (if transactional)
            if (transactionReadSet is not null)
            {
                await ValidateReadSetAsync(transactionReadSet, cancellationToken).ConfigureAwait(false);
            }

            // 2. Prepare phase: validate all preconditions and build mutations (no store writes)
            var now = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
            var mutations = new List<PreparedMutation>(writes.Count);

            foreach (var write in writes)
            {
                var mutation = await PrepareWriteAsync(write, now, cancellationToken).ConfigureAwait(false);
                mutations.Add(mutation);
            }

            // 3. Apply phase: execute all mutations (infallible after prepare)
            var results = new List<WriteResult>(mutations.Count);
            var documentMutations = new List<DocumentMutation>(mutations.Count);

            foreach (var mutation in mutations)
            {
                await ApplyMutationAsync(mutation, cancellationToken).ConfigureAwait(false);
                results.Add(mutation.Result);
                documentMutations.Add(new DocumentMutation(
                    mutation.Path.ResourceName,
                    mutation.Type == MutationType.Delete ? null : mutation.Document));
            }

            if (documentMutations is { Count: > 0 })
            {
                changeNotifier.NotifyDocumentsChanged(documentMutations);
            }

            return results;
        }
        finally
        {
            _commitLock.Release();
        }
    }

    private async Task ValidateReadSetAsync(
        IReadOnlyDictionary<string, Timestamp?> readSet,
        CancellationToken cancellationToken)
    {
        foreach (var (resourceName, expectedUpdateTime) in readSet)
        {
            var path = DocumentPath.Parse(resourceName);
            var current = await store.TryGetAsync(path, cancellationToken).ConfigureAwait(false);

            var currentUpdateTime = current?.UpdateTime;

            var conflict = (expectedUpdateTime, currentUpdateTime) switch
            {
                (null, null) => false,       // both missing — no conflict
                (null, _) => true,           // was missing, now exists
                (_, null) => true,           // existed, now missing
                _ => !expectedUpdateTime.Equals(currentUpdateTime)
            };

            if (conflict)
            {
                throw new RpcException(new Status(StatusCode.Aborted,
                    $"Transaction conflict: document '{resourceName}' was modified by another operation."));
            }
        }
    }

    private async Task<PreparedMutation> PrepareWriteAsync(Write write, Timestamp now, CancellationToken cancellationToken)
    {
        switch (write.OperationCase)
        {
            case Write.OperationOneofCase.Update:
                {
                    var path = DocumentPath.Parse(write.Update.Name);
                    var existing = await store.TryGetAsync(path, cancellationToken).ConfigureAwait(false);

                    CheckPrecondition(existing, write.CurrentDocument, path.ResourceName);

                    Document updated;
                    var maskPaths = write.UpdateMask?.FieldPaths;

                    if (existing is null)
                    {
                        updated = write.Update.Clone();
                        updated.Name = path.ResourceName;
                        updated.CreateTime = now;
                        updated.UpdateTime = now;

                        if (maskPaths is { Count: > 0 })
                        {
                            updated.Fields.Clear();
                            foreach (var rawPath in maskPaths)
                            {
                                var fieldPath = FieldPath.Parse(rawPath);
                                var val = DocumentNavigator.GetValue(write.Update, fieldPath);
                                if (val is not null)
                                {
                                    DocumentNavigator.SetValue(updated, fieldPath, val);
                                }
                            }
                        }

                        return new PreparedMutation(MutationType.Create, path, updated, new WriteResult { UpdateTime = now });
                    }
                    else
                    {
                        updated = existing.Clone();
                        updated.UpdateTime = now;

                        if (maskPaths is { Count: > 0 })
                        {
                            foreach (var rawPath in maskPaths)
                            {
                                var fieldPath = FieldPath.Parse(rawPath);
                                var val = DocumentNavigator.GetValue(write.Update, fieldPath);
                                if (val is not null)
                                {
                                    DocumentNavigator.SetValue(updated, fieldPath, val);
                                }
                                else
                                {
                                    DocumentNavigator.RemoveValue(updated, fieldPath);
                                }
                            }
                        }
                        else
                        {
                            updated.Fields.Clear();
                            updated.Fields.Add(write.Update.Fields);
                        }

                        return new PreparedMutation(MutationType.Update, path, updated, new WriteResult { UpdateTime = now });
                    }
                }

            case Write.OperationOneofCase.Delete:
                {
                    var path = DocumentPath.Parse(write.Delete);
                    var existing = await store.TryGetAsync(path, cancellationToken).ConfigureAwait(false);

                    CheckPrecondition(existing, write.CurrentDocument, path.ResourceName);

                    return existing is not null
                        ? new PreparedMutation(MutationType.Delete, path, null, new WriteResult())
                        : new PreparedMutation(MutationType.None, path, null, new WriteResult());
                }

            case Write.OperationOneofCase.Transform:
                throw new RpcException(new Status(StatusCode.Unimplemented, "Transform writes are not supported."));

            default:
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Write must have an operation set."));
        }
    }

    private async Task ApplyMutationAsync(PreparedMutation mutation, CancellationToken cancellationToken)
    {
        switch (mutation.Type)
        {
            case MutationType.Create:
                await store.CreateAsync(mutation.Path, mutation.Document!, cancellationToken).ConfigureAwait(false);
                break;
            case MutationType.Update:
                await store.UpdateAsync(mutation.Path, mutation.Document!, cancellationToken).ConfigureAwait(false);
                break;
            case MutationType.Delete:
                await store.DeleteAsync(mutation.Path, cancellationToken).ConfigureAwait(false);
                break;
            case MutationType.None:
                break;
        }
    }

    private enum MutationType { None, Create, Update, Delete }

    private sealed record PreparedMutation(MutationType Type, DocumentPath Path, Document? Document, WriteResult Result);
}

