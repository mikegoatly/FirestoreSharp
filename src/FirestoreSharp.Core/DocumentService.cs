using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;

namespace FirestoreSharp.Core;

internal sealed class DocumentService(IDocumentStore store) : IDocumentService
{
    public async Task<Document> CreateAsync(FirestorePath path, Document document, CancellationToken cancellationToken = default)
    {
        var now = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        var created = document.Clone();
        created.Name = path.ResourceName;
        created.CreateTime = now;
        created.UpdateTime = now;

        await store.CreateAsync(path, created, cancellationToken).ConfigureAwait(false);

        return created;
    }

    public async Task<Document> GetAsync(FirestorePath path, CancellationToken cancellationToken = default)
    {
        return await store.GetAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<BatchGetResult> BatchGetAsync(IReadOnlyList<string> resourceNames, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var resourceName in resourceNames)
        {
            var path = FirestorePath.Parse(resourceName);
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

    public async Task<Document> UpdateAsync(FirestorePath path, Document document, IReadOnlyList<string>? updateMaskFieldPaths, CancellationToken cancellationToken = default)
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

    public async Task DeleteAsync(FirestorePath path, CancellationToken cancellationToken = default)
    {
        await store.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
    }
}
