using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core.Listeners;

/// <summary>
/// Base class for a registered listener target, tracking which documents are currently "in view."
/// </summary>
internal abstract class ListenerTarget(int targetId)
{
    public int TargetId { get; } = targetId;

    /// <summary>
    /// The set of document resource names currently considered "in view" for this target.
    /// Used to detect when a document enters or leaves the target's result set.
    /// </summary>
    public HashSet<string> ActiveDocuments { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// A target that watches specific documents by their resource names.
/// </summary>
internal sealed class DocumentListenerTarget(int targetId, IReadOnlyList<string> documentNames)
    : ListenerTarget(targetId)
{
    public IReadOnlyList<string> DocumentNames { get; } = documentNames;
}

/// <summary>
/// A target that watches documents matching a structured query.
/// </summary>
internal sealed class QueryListenerTarget(int targetId, string parent, StructuredQuery query)
    : ListenerTarget(targetId)
{
    public string Parent { get; } = parent;
    public StructuredQuery Query { get; } = query;
}
