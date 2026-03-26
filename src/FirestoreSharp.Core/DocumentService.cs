using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;

namespace FirestoreSharp.Core;

public sealed class DocumentService(IDocumentStore store)
{
    public async Task<Document> CreateAsync(FirestorePath path, Document document, CancellationToken cancellationToken = default)
    {
        var now = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        var created = document.Clone();
        created.Name = path.ResourceName;
        created.CreateTime = now;
        created.UpdateTime = now;

        await store.CreateAsync(path, created, cancellationToken);

        return created;
    }

    public async Task<Document> GetAsync(FirestorePath path, CancellationToken cancellationToken = default)
    {
        return await store.GetAsync(path, cancellationToken);
    }

    public async Task<Document> UpdateAsync(FirestorePath path, Document document, IReadOnlyList<string>? updateMaskFieldPaths, CancellationToken cancellationToken = default)
    {
        var existing = await store.GetAsync(path, cancellationToken);

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

        return await store.UpdateAsync(path, updated, cancellationToken);
    }
}
