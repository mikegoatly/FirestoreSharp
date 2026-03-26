namespace FirestoreSharp.Core;

/// <summary>
/// Strongly-typed representation of a Firestore resource name.
/// Format: projects/{project}/databases/{database}/documents/{documentPath}
/// where documentPath is {collection}/{docId} or {collection}/{docId}/{subCollection}/{subDocId}/...
/// </summary>
public sealed class FirestorePath
{
    public string Project { get; }
    public string Database { get; }

    /// <summary>
    /// The collection segments (e.g. ["users"] or ["users", "u1", "posts"] for subcollections).
    /// Always has an odd number of elements: col1, doc1, col2, doc2, ..., colN.
    /// The last segment is the collection containing the document.
    /// </summary>
    public IReadOnlyList<string> CollectionPath { get; }
    public string DocumentId { get; }

    /// <summary>The full resource name as expected by the Firestore API.</summary>
    public string ResourceName { get; }

    private FirestorePath(string project, string database, IReadOnlyList<string> collectionPath, string documentId, string? resourceName = null)
    {
        Project = project;
        Database = database;
        CollectionPath = collectionPath;
        DocumentId = documentId;
        ResourceName = resourceName ?? $"projects/{Project}/databases/{Database}/documents/{string.Join("/", CollectionPath)}/{DocumentId}";
    }

    /// <summary>
    /// Parses a full resource name into a <see cref="FirestorePath"/>.
    /// </summary>
    public static FirestorePath Parse(string resourceName)
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

        // Everything after "documents" is the document path: col/docId or col/docId/subCol/subDocId/...
        // CollectionPath contains all segments except the final document ID, so its count is (docPathLength - 1).
        // This results in an odd count for simple paths, but always maintains the alternating collection/document pattern for nested paths.
        var docPathLength = parts.Length - 5;
        if (docPathLength < 2 || docPathLength % 2 != 0)
        {
            throw new ArgumentException(
                $"Document path must have an even number of segments (collection/document pairs), got {docPathLength}: '{resourceName}'",
                nameof(resourceName));
        }

        // Collection path is everything except the last segment (which is the document ID)
        var collectionPath = new string[docPathLength - 1];
        for (var i = 0; i < collectionPath.Length; i++)
        {
            var segment = parts[5 + i];
            ValidateSegment(segment, $"path segment {i}", resourceName);
            collectionPath[i] = segment;
        }

        var documentId = parts[^1];
        ValidateSegment(documentId, "document ID", resourceName);

        return new FirestorePath(project, database, collectionPath, documentId, resourceName);
    }

    /// <summary>
    /// Builds a <see cref="FirestorePath"/> from a CreateDocument-style request
    /// (parent + collectionId + documentId).
    /// </summary>
    public static FirestorePath FromCreateRequest(string parent, string collectionId, string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        return Parse($"{parent}/{collectionId}/{documentId}");
    }

    /// <summary>
    /// Returns the relative path segments for storage — project, database, then the document path.
    /// E.g. ["p1", "(default)", "users", "u1"] for projects/p1/databases/(default)/documents/users/u1.
    /// </summary>
    public ReadOnlySpan<string> ToStorageSegments()
    {
        var segments = new string[2 + CollectionPath.Count + 1];
        segments[0] = Project;
        segments[1] = Database;
        for (var i = 0; i < CollectionPath.Count; i++)
        {
            segments[2 + i] = CollectionPath[i];
        }

        segments[^1] = DocumentId;
        return segments;
    }

    public override string ToString() => ResourceName;

    private static void ValidateSegment(string segment, string label, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            throw new ArgumentException($"Empty {label} in resource name: '{fullPath}'", nameof(fullPath));
        }

        if (segment.Contains('/'))
        {
            throw new ArgumentException($"Invalid {label} contains '/': '{fullPath}'", nameof(fullPath));
        }
    }
}
