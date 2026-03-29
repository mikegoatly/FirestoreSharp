namespace FirestoreSharp.Core;

/// <summary>
/// Strongly-typed representation of a Firestore database resource path.
/// Format: projects/{project}/databases/{database}
/// </summary>
public readonly record struct DatabasePath
{
    /// <summary>The full database resource name, e.g. <c>projects/p/databases/(default)</c>.</summary>
    public ReadOnlyMemory<char> ResourceName { get; }

    /// <summary>The project ID.</summary>
    public string Project { get; }

    /// <summary>The database ID.</summary>
    public string Database { get; }

    private DatabasePath(ReadOnlyMemory<char> resourceName, string project, string database)
    {
        ResourceName = resourceName;
        Project = project;
        Database = database;
    }

    /// <summary>
    /// Internal constructor for callers (e.g. <see cref="CollectionPath"/>) that have already
    /// parsed and allocated the project and database strings.
    /// </summary>
    internal static DatabasePath FromParsed(ReadOnlyMemory<char> resourceName, string project, string database)
        => new(resourceName, project, database);

    /// <inheritdoc cref="Parse(ReadOnlyMemory{char})"/>
    public static DatabasePath Parse(string resourceName)
    {
        ArgumentNullException.ThrowIfNull(resourceName);
        return Parse(resourceName.AsMemory());
    }

    /// <summary>
    /// Parses a database resource name into a <see cref="DatabasePath"/>.
    /// </summary>
    public static DatabasePath Parse(ReadOnlyMemory<char> resourceName)
    {
        var remaining = resourceName.Span;

        if (!ResourcePathParser.TryConsume(ref remaining, "projects/"))
        {
            ResourcePathParser.ThrowFormat(resourceName, "database path", "expected 'projects/' prefix");
        }

        var project = ResourcePathParser.ReadSegment(ref remaining, resourceName, "database path").ToString();

        if (!ResourcePathParser.TryConsume(ref remaining, "databases/"))
        {
            ResourcePathParser.ThrowFormat(resourceName, "database path", "expected 'databases/' segment");
        }

        var database = ResourcePathParser.ReadFinalSegment(ref remaining, resourceName, "database path").ToString();

        return new DatabasePath(resourceName, project, database);
    }

    /// <summary>
    /// Returns the full documents root for this database, e.g.
    /// <c>projects/p/databases/(default)/documents</c>.
    /// Allocates a new string.
    /// </summary>
    public string DocumentsRoot => $"{ResourceName}/documents";

    /// <summary>
    /// Returns <c>true</c> if <paramref name="parent"/> is a database documents root
    /// (i.e. <c>projects/{p}/databases/{d}/documents</c> with no further segments),
    /// and outputs the parsed <see cref="DatabasePath"/> if so.
    /// </summary>
    public static bool IsDatabaseRoot(string parent, out DatabasePath databasePath)
    {
        ArgumentNullException.ThrowIfNull(parent);
        const string documentsMarker = "/documents";
        if (parent.EndsWith(documentsMarker, StringComparison.Ordinal))
        {
            var candidate = parent.AsMemory()[..^documentsMarker.Length];
            if (TryParse(candidate, out databasePath))
            {
                return true;
            }
        }

        databasePath = default;
        return false;
    }

    private static bool TryParse(ReadOnlyMemory<char> resourceName, out DatabasePath result)
    {
        var remaining = resourceName.Span;

        if (!ResourcePathParser.TryConsume(ref remaining, "projects/"))
        {
            result = default; 
            return false;
        }

        var slash = remaining.IndexOf('/');
        if (slash <= 0)
        {
            result = default;
            return false;
        }
        var project = remaining[..slash].ToString();
        remaining = remaining[(slash + 1)..];

        if (!ResourcePathParser.TryConsume(ref remaining, "databases/"))
        {
            result = default; 
            return false;
        }

        if (remaining.IsEmpty)
        {
            result = default; 
            return false;
        }
        var database = remaining.ToString();

        result = new DatabasePath(resourceName, project, database);
        return true;
    }

    public bool Equals(DatabasePath other) =>
        ResourceName.Span.Equals(other.ResourceName.Span, StringComparison.Ordinal);

    public override int GetHashCode() => string.GetHashCode(ResourceName.Span, StringComparison.Ordinal);

    public override string ToString() => ResourceName.ToString();
}
