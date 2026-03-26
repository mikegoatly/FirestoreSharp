namespace FirestoreSharp.Core;

/// <summary>
/// Strongly-typed representation of a Firestore document resource name.
/// Format: projects/{project}/databases/{database}/documents/{collectionSegments}/{documentId}
/// where collectionSegments is an even number of path components (collection/docId pairs).
/// </summary>
public sealed class DocumentPath
{
    public string Project { get; }
    public string Database { get; }

    /// <summary>The parent collection of this document.</summary>
    public CollectionPath Collection { get; }

    public string DocumentId { get; }

    /// <summary>The full resource name as expected by the Firestore API.</summary>
    public string ResourceName { get; }

    private DocumentPath(CollectionPath collection, string documentId, string resourceName)
    {
        Project = collection.Project;
        Database = collection.Database;
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

        var parts = resourceName.Split('/');

        // Minimum: projects/{p}/databases/{d}/documents/{col}/{id} = 7 segments
        if (parts.Length < 7)
        {
            throw new ArgumentException($"Resource name has too few segments: '{resourceName}'", nameof(resourceName));
        }

        if (parts[0] != "projects")
        {
            throw new ArgumentException($"Expected 'projects' at segment 0, got '{parts[0]}'", nameof(resourceName));
        }

        if (parts[2] != "databases")
        {
            throw new ArgumentException($"Expected 'databases' at segment 2, got '{parts[2]}'", nameof(resourceName));
        }

        if (parts[4] != "documents")
        {
            throw new ArgumentException($"Expected 'documents' at segment 4, got '{parts[4]}'", nameof(resourceName));
        }

        var project = parts[1];
        var database = parts[3];

        ValidateSegment(project, "project", resourceName);
        ValidateSegment(database, "database", resourceName);

        // Everything after "documents" is the document path: col/docId[/subCol/subDocId/...]
        var docPathLength = parts.Length - 5;
        if (docPathLength < 2 || docPathLength % 2 != 0)
        {
            throw new ArgumentException(
                $"Document path must have an even number of segments (collection/document pairs), got {docPathLength}: '{resourceName}'",
                nameof(resourceName));
        }

        // Validate all path segments (collection segments + document ID)
        for (var i = 5; i < parts.Length; i++)
        {
            ValidateSegment(parts[i], $"path segment {i - 5}", resourceName);
        }

        var documentId = parts[^1];

        // Slice the collection resource name directly — no joining/copying
        var collectionResourceName = resourceName[..^(documentId.Length + 1)];
        // segmentsStart: length of "projects/{p}/databases/{d}/documents/" prefix
        var segmentsStart = parts[0].Length + parts[1].Length + parts[2].Length + parts[3].Length + parts[4].Length + 5;
        var collection = CollectionPath.FromValidatedParts(project, database, collectionResourceName, segmentsStart, docPathLength - 1);

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
        var collSegs = Collection.ToStorageSegments();
        var result = new string[collSegs.Length + 1];
        collSegs.CopyTo(result);
        result[^1] = DocumentId;
        return result;
    }

    public override string ToString() => ResourceName;

}

