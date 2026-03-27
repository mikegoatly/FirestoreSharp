using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core.Listeners;

/// <summary>
/// Represents a single document mutation — a create, update, or delete.
/// </summary>
/// <param name="ResourceName">The full resource name of the mutated document.</param>
/// <param name="NewState">The document's new state, or <c>null</c> if the document was deleted.</param>
public readonly record struct DocumentMutation(string ResourceName, Document? NewState);

/// <summary>
/// Accepts notifications about document mutations so that active listeners can be updated.
/// Implementations must be safe to call from any thread and must not block.
/// </summary>
public interface IDocumentChangeNotifier
{
    void NotifyDocumentsChanged(IReadOnlyList<DocumentMutation> mutations);
}
