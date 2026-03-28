using FirestoreSharp.Core.Stores.Overlay;

using Google.Cloud.Firestore.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace FirestoreSharp.Core.Transactions;

internal sealed class TransactionState(ByteString id, TransactionOptions.ModeOneofCase mode, DateTimeOffset startTime, OverlayStore? overlay)
{
    public ByteString Id { get; } = id;
    public TransactionOptions.ModeOneofCase Mode { get; } = mode;
    public DateTimeOffset StartTime { get; } = startTime;
    public DateTimeOffset ExpiresAt { get; } = startTime.AddSeconds(60);

    /// <summary>
    /// The overlay store for this transaction. Only set for read-write transactions.
    /// Null for read-only transactions.
    /// </summary>
    public OverlayStore? Overlay { get; } = overlay;

    /// <summary>
    /// Tracks documents read during this transaction.
    /// Key = document resource name, Value = UpdateTime at read (null if document was missing).
    /// </summary>
    private readonly Dictionary<string, Timestamp?> _readSet = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public void RecordRead(string documentResourceName, Timestamp? updateTime)
    {
        lock (_lock)
        {
            // Only record the first read of a document (consistent with snapshot semantics)
            _readSet.TryAdd(documentResourceName, updateTime);
        }
    }

    public IReadOnlyDictionary<string, Timestamp?> GetReadSet()
    {
        lock (_lock)
        {
            return new Dictionary<string, Timestamp?>(_readSet, StringComparer.Ordinal);
        }
    }
}
