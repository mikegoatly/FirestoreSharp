namespace FirestoreSharp.Core;

/// <summary>
/// Strongly-typed representation of a Firestore document resource name.
/// Format: projects/{project}/databases/{database}/documents/{collectionSegments}/{documentId}
/// where collectionSegments is an even number of path components (collection/docId pairs).
/// </summary>
public sealed class DocumentPath
{
    public string Project => Collection.Project;
    public string Database => Collection.Database;

    /// <summary>The parent collection of this document.</summary>
    public CollectionPath Collection { get; }

    public string DocumentId { get; }

    /// <summary>The full resource name as expected by the Firestore API.</summary>
    public string ResourceName { get; }

    private DocumentPath(CollectionPath collection, string documentId, string resourceName)
    {
        Collection = collection;
        DocumentId = documentId;
        ResourceName = resourceName;
    }

    /// <summary>
    /// Parses a full document resource name into a <see cref="DocumentPath"/>.
    /// </summary>
    public static DocumentPath Parse(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var lastSlash = resourceName.LastIndexOf('/');
        if (lastSlash < 0)
        {
            throw new ArgumentException($"Invalid document path: '{resourceName}'", nameof(resourceName));
        }

        var documentId = resourceName[(lastSlash + 1)..];
        if (documentId.Length == 0 || documentId.AsSpan().IsWhiteSpace())
        {
            throw new ArgumentException($"Invalid document path (empty or whitespace document ID): '{resourceName}'", nameof(resourceName));
        }

        // CollectionPath.Parse validates the projects/.../databases/.../documents/... prefix
        // and requires an odd segment count — exactly the shape of the collection part of a
        // valid document resource name. An even total doc-path length (the document requirement)
        // follows automatically: odd collection segments + 1 document ID = even.
        var collection = CollectionPath.Parse(resourceName.AsMemory()[..lastSlash]);

        return new DocumentPath(collection, documentId, resourceName);
    }

    /// <summary>
    /// Builds a <see cref="DocumentPath"/> from a CreateDocument-style request
    /// (parent + collectionId + documentId).
    /// </summary>
    public static DocumentPath FromCreateRequest(string parent, string collectionId, string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        return Parse($"{parent}/{collectionId}/{documentId}");
    }

    /// <summary>
    /// Returns storage segments: [project, database, …collectionSegments, documentId].
    /// </summary>
    public ReadOnlySpan<string> ToStorageSegments()
    {
        var result = Collection.ToStorageSegments(endPadding: 1);
        result[^1] = DocumentId;
        return result;
    }



    public override string ToString() => ResourceName;

}

