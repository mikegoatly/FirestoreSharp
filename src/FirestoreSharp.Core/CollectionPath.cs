namespace FirestoreSharp.Core;

/// <summary>
/// Strongly-typed representation of a Firestore collection resource path.
/// Format: projects/{project}/databases/{database}/documents/{segments}
/// where {segments} is an odd number of path components (≥ 1) ending at a collection name,
/// e.g. <c>users</c> or <c>users/u1/posts</c>.
/// </summary>
public sealed class CollectionPath
{
    // Offset within ResourceName where the first segment character starts
    // (i.e. immediately after "projects/{p}/databases/{d}/documents/").
    private readonly int _segmentsStart;
    private readonly int _segmentCount;
    private string[]? _segments; // lazily materialised on first Segments access

    public string Project { get; }
    public string Database { get; }

    /// <summary>The full collection resource name, e.g. <c>projects/p/databases/d/documents/users</c>.</summary>
    public ReadOnlyMemory<char> ResourceName { get; }

    /// <summary>
    /// The path segments after <c>documents/</c>.
    /// Odd count: alternating collection and document ID names, ending with a collection name.
    /// E.g. <c>["users"]</c> for a top-level collection, or <c>["users", "u1", "posts"]</c>
    /// for a subcollection.
    /// The backing array is allocated on first access; use <see cref="HasCollectionAfter"/>
    /// to query segments without materialising.
    /// </summary>
    public IReadOnlyList<string> Segments => GetMaterialisedSegments();

    private CollectionPath(string project, string database, ReadOnlyMemory<char> resourceName, int segmentsStart, int segmentCount)
    {
        Project = project;
        Database = database;
        ResourceName = resourceName;
        _segmentsStart = segmentsStart;
        _segmentCount = segmentCount;
    }

    /// <inheritdoc cref="Parse(ReadOnlyMemory{char})"/>
    public static CollectionPath Parse(string resourceName)
    {
        ArgumentNullException.ThrowIfNull(resourceName);
        return Parse(resourceName.AsMemory());
    }

    /// <summary>
    /// Parses a collection resource name into a <see cref="CollectionPath"/>.
    /// The path must have an odd number of segments (≥ 1) after <c>documents</c>.
    /// Parsing is span-based and allocates only the two extracted strings (project, database).
    /// </summary>
    public static CollectionPath Parse(ReadOnlyMemory<char> resourceName)
    {
        var remaining = resourceName.Span;

        if (!TryConsume(ref remaining, "projects/"))
        {
            ThrowFormat(resourceName, "expected 'projects/' prefix");
        }

        var project = ReadSegment(ref remaining, resourceName);

        if (!TryConsume(ref remaining, "databases/"))
        {
            ThrowFormat(resourceName, "expected 'databases/' segment");
        }

        var database = ReadSegment(ref remaining, resourceName);

        if (!TryConsume(ref remaining, "documents/"))
        {
            ThrowFormat(resourceName, "expected 'documents/' followed by at least one segment");
        }

        if (remaining.IsEmpty)
        {
            ThrowFormat(resourceName, "no segments after 'documents'");
        }

        var segmentsStart = resourceName.Length - remaining.Length;
        var segmentCount = CountAndValidateSegments(remaining, resourceName);

        if (segmentCount % 2 == 0)
        {
            ThrowFormat(resourceName, $"collection path requires an odd segment count after 'documents', got {segmentCount}");
        }

        return new CollectionPath(project.ToString(), database.ToString(), resourceName, segmentsStart, segmentCount);
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="documentName"/> is a direct child of this collection —
    /// i.e. it starts with this collection's resource name followed by exactly one additional path segment (the document ID).
    /// </summary>
    public bool IsDirectChildDocument(string documentName)
    {
        ArgumentNullException.ThrowIfNull(documentName);

        var rn = ResourceName.Span;
        var dn = documentName.AsSpan();

        if (dn.Length <= rn.Length || dn[rn.Length] != '/' || !dn[..rn.Length].Equals(rn, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = dn[(rn.Length + 1)..];
        return remainder.Length > 0 && remainder.IndexOf('/') < 0;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="collectionId"/> appears as a collection name
    /// at or after <paramref name="segmentOffset"/> in <see cref="Segments"/>.
    /// Collection names occupy even indices (0, 2, 4, …) in <see cref="Segments"/>.
    /// Scans the raw resource name span — does not allocate <see cref="Segments"/>.
    /// </summary>
    public bool HasCollectionAfter(int segmentOffset, string collectionId)
    {
        var span = ResourceName.Span[_segmentsStart..];
        var target = collectionId.AsSpan();
        var currentIndex = 0;

        while (!span.IsEmpty)
        {
            var slash = span.IndexOf('/');
            var segment = slash < 0 ? span : span[..slash];

            if (currentIndex >= segmentOffset
                && (currentIndex - segmentOffset) % 2 == 0
                && segment.Equals(target, StringComparison.Ordinal))
            {
                return true;
            }

            if (slash < 0)
            {
                break;
            }

            span = span[(slash + 1)..];
            currentIndex++;
        }

        return false;
    }

    /// <summary>
    /// Returns storage segments for this collection: [project, database, …path segments].
    /// </summary>
    public ReadOnlySpan<string> ToStorageSegments()
    {
        return ToStorageSegments(0);
    }

    internal string[] ToStorageSegments(int endPadding)
    {
        var segs = GetMaterialisedSegments();
        var result = new string[2 + segs.Length + endPadding];
        result[0] = Project;
        result[1] = Database;

        Array.Copy(segs, 0, result, 2, segs.Length);

        return result;
    }

    public override string ToString() => ResourceName.ToString();

    private string[] GetMaterialisedSegments()
    {
        _segments ??= MaterialiseSegments();
        return _segments;
    }

    private string[] MaterialiseSegments()
    {
        var segments = new string[_segmentCount];
        var span = ResourceName.Span[_segmentsStart..];
        for (var i = 0; i < _segmentCount; i++)
        {
            var slash = span.IndexOf('/');
            if (slash < 0)
            {
                segments[i] = span.ToString();
                break;
            }
            segments[i] = span[..slash].ToString();
            span = span[(slash + 1)..];
        }
        return segments;
    }

    // Advances span past prefix; returns false (without advancing) if not matched.
    private static bool TryConsume(ref ReadOnlySpan<char> span, string prefix)
    {
        if (!span.StartsWith(prefix.AsSpan(), StringComparison.Ordinal))
        {
            return false;
        }

        span = span[prefix.Length..];
        return true;
    }

    // Reads a non-empty, non-whitespace segment up to the next '/', advancing span past both.
    private static ReadOnlySpan<char> ReadSegment(ref ReadOnlySpan<char> span, ReadOnlyMemory<char> fullPath)
    {
        var slash = span.IndexOf('/');
        if (slash < 0)
        {
            ThrowFormat(fullPath, "unexpected end of path");
        }

        var seg = span[..slash];
        if (seg.IsEmpty || seg.Trim().IsEmpty)
        {
            ThrowFormat(fullPath, "empty or whitespace path segment");
        }

        span = span[(slash + 1)..];
        return seg;
    }

    // Counts segments separated by '/', validating each is non-empty and non-whitespace.
    private static int CountAndValidateSegments(ReadOnlySpan<char> span, ReadOnlyMemory<char> fullPath)
    {
        var count = 0;
        var segStart = 0;
        for (var i = 0; i <= span.Length; i++)
        {
            if (i == span.Length || span[i] == '/')
            {
                var seg = span[segStart..i];
                if (seg.IsEmpty || seg.Trim().IsEmpty)
                {
                    ThrowFormat(fullPath, "empty or whitespace segment");
                }

                count++;
                segStart = i + 1;
            }
        }
        return count;
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowFormat(ReadOnlyMemory<char> resourceName, string reason) =>
        throw new ArgumentException($"Invalid collection path ({reason}): '{resourceName}'", nameof(resourceName));
}
