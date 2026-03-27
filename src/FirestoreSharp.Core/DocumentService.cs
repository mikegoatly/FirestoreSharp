using FirestoreSharp.Core.Query;
using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace FirestoreSharp.Core;

internal sealed class DocumentService(IDocumentStore store) : IDocumentService
{
    public async Task<Document> CreateAsync(DocumentPath path, Document document, CancellationToken cancellationToken = default)
    {
        var now = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        var created = document.Clone();
        created.Name = path.ResourceName;
        created.CreateTime = now;
        created.UpdateTime = now;

        await store.CreateAsync(path, created, cancellationToken).ConfigureAwait(false);

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

        return await store.UpdateAsync(path, updated, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(DocumentPath path, CancellationToken cancellationToken = default)
    {
        await store.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
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
                                DocumentNavigator.SetValue(updated, fieldPath, val);
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
                                DocumentNavigator.SetValue(updated, fieldPath, val);
                            else
                                DocumentNavigator.RemoveValue(updated, fieldPath);
                        }
                    }
                    else
                    {
                        updated.Fields.Clear();
                        updated.Fields.Add(write.Update.Fields);
                    }

                    await store.UpdateAsync(path, updated, cancellationToken).ConfigureAwait(false);
                }

                return new WriteResult { UpdateTime = now };
            }

            case Write.OperationOneofCase.Delete:
            {
                var path = DocumentPath.Parse(write.Delete);
                var existing = await store.TryGetAsync(path, cancellationToken).ConfigureAwait(false);

                CheckPrecondition(existing, write.CurrentDocument, path.ResourceName);

                if (existing is not null)
                    await store.DeleteAsync(path, cancellationToken).ConfigureAwait(false);

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
            return;

        switch (precondition.ConditionTypeCase)
        {
            case Precondition.ConditionTypeOneofCase.Exists when precondition.Exists:
                if (existing is null)
                    throw new RpcException(new Status(StatusCode.FailedPrecondition,
                        $"Document '{resourceName}' does not exist."));
                break;

            case Precondition.ConditionTypeOneofCase.Exists:
                if (existing is not null)
                    throw new RpcException(new Status(StatusCode.FailedPrecondition,
                        $"Document '{resourceName}' already exists."));
                break;

            case Precondition.ConditionTypeOneofCase.UpdateTime:
                if (existing is null)
                    throw new RpcException(new Status(StatusCode.FailedPrecondition,
                        $"Document '{resourceName}' does not exist."));
                if (!existing.UpdateTime.Equals(precondition.UpdateTime))
                    throw new RpcException(new Status(StatusCode.FailedPrecondition,
                        $"Document '{resourceName}' update time does not match precondition."));
                break;
        }
    }
}

