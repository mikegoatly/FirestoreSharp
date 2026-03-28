using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core.Stores.Overlay;

/// <summary>
/// Represents one slot in a transaction's overlay store.
/// </summary>
/// <param name="Document">The buffered document state, or null if this is a tombstone.</param>
/// <param name="IsDeleted">True if the document was deleted within this transaction.</param>
/// <param name="IsDirty">True if this entry was written (not merely read-promoted) within this transaction.</param>
internal sealed record OverlayEntry(Document? Document, bool IsDeleted, bool IsDirty);
