using System.Collections.Concurrent;

using FirestoreSharp.Core.Stores.Overlay;

using Google.Cloud.Firestore.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

using Microsoft.Extensions.Logging;

namespace FirestoreSharp.Core.Transactions;

internal sealed partial class TransactionManager(IDocumentStore baseStore, ILogger<TransactionManager> logger) : ITransactionManager
{
    private readonly ConcurrentDictionary<ByteString, TransactionState> _active = new();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Begin transaction {TransactionId} ({Mode})")]
    private partial void LogBegin(string transactionId, TransactionOptions.ModeOneofCase mode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Commit transaction {TransactionId}")]
    private partial void LogComplete(string transactionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rollback transaction {TransactionId}")]
    private partial void LogRollback(string transactionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transaction {TransactionId} expired")]
    private partial void LogExpired(string transactionId);

    // 4-byte hex prefix is a cheap, stable short ID for log correlation
    private static string TxId(ByteString id) => Convert.ToHexString(id.Span[..Math.Min(4, id.Length)]);

    public ByteString BeginTransaction(TransactionOptions? options)
    {
        CleanupExpired();

        var mode = options?.ModeCase ?? TransactionOptions.ModeOneofCase.ReadWrite;
        var id = ByteString.New();

        // Read-write transactions get an overlay store for snapshot isolation.
        // Read-only transactions don't write, so no overlay is needed.
        var overlay = mode == TransactionOptions.ModeOneofCase.ReadWrite
            ? new OverlayStore(baseStore)
            : null;

        var state = new TransactionState(id, mode, DateTimeOffset.UtcNow, overlay);

        _active[id] = state;
        LogBegin(TxId(id), mode);
        return id;
    }

    private TransactionState GetTransaction(ByteString id)
    {
        if (!_active.TryGetValue(id, out var state))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Transaction not found or has already been committed/rolled back."));
        }

        if (state.IsExpired(DateTimeOffset.UtcNow))
        {
            _active.TryRemove(id, out _);
            LogExpired(TxId(id));
            throw new RpcException(new Status(StatusCode.Aborted,
                "Transaction has expired due to inactivity."));
        }

        return state;
    }

    public void RecordRead(ByteString transactionId, string documentResourceName, Timestamp? updateTime)
    {
        var state = GetTransaction(transactionId);
        state.RecordRead(documentResourceName, updateTime);
    }

    public IReadOnlyDictionary<string, Timestamp?> GetReadSet(ByteString transactionId)
    {
        var state = GetTransaction(transactionId);
        return state.GetReadSet();
    }

    public IDocumentStore? GetOverlay(ByteString transactionId)
    {
        var state = GetTransaction(transactionId);
        return state.Overlay;
    }

    public void Complete(ByteString transactionId)
    {
        _active.TryRemove(transactionId, out _);
        LogComplete(TxId(transactionId));
    }

    /// <summary>
    /// Validates the transaction exists and is not expired, then removes it.
    /// Used by Rollback.
    /// </summary>
    public void ValidateAndComplete(ByteString transactionId)
    {
        GetTransaction(transactionId);
        _active.TryRemove(transactionId, out _);
        LogRollback(TxId(transactionId));
    }

    public void ValidateCanWrite(ByteString transactionId)
    {
        var state = GetTransaction(transactionId);
        if (state.Mode == TransactionOptions.ModeOneofCase.ReadOnly)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Cannot commit writes in a read-only transaction."));
        }
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _active)
        {
            if (kvp.Value.IsExpired(now))
            {
                _active.TryRemove(kvp.Key, out _);
            }
        }
    }
}
