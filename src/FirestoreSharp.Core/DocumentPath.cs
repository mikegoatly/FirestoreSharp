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

        if (!TryParse(resourceName, out var result, out var error))
        {
            ResourcePathParser.ThrowFormat(resourceName.AsMemory(), "document path", error);
        }

        return result;
    }

    /// <summary>
    /// Returns a parsed <see cref="DocumentPath"/> if <paramref name="resourceName"/> is a valid
    /// document path, or <c>null</c> if it is not (e.g. a collection path or database root).
    /// </summary>
    public static DocumentPath? TryParse(string resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            return null;
        }

        return TryParse(resourceName, out var result, out _) ? result : null;
    }

    private static bool TryParse(string resourceName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DocumentPath? result, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error)
    {
        result = null;

        var lastSlash = resourceName.LastIndexOf('/');
        if (lastSlash < 0)
        {
            error = "missing '/'";
            return false;
        }

        var documentId = resourceName[(lastSlash + 1)..];
        if (documentId.Length == 0 || documentId.AsSpan().IsWhiteSpace())
        {
            error = "empty or whitespace document ID";
            return false;
        }

        // CollectionPath.TryParse validates the prefix and requires an odd segment count —
        // exactly the shape of the collection part of a valid document resource name.
        if (!CollectionPath.TryParse(resourceName.AsMemory()[..lastSlash], out var collection, out error))
        {
            return false;
        }

        result = new DocumentPath(collection, documentId, resourceName);
        error = null;
        return true;
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

