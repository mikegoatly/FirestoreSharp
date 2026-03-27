using System.Collections.Concurrent;
using Google.Cloud.Firestore.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace FirestoreSharp.Core.Transactions;

internal sealed class TransactionManager : ITransactionManager
{
    private readonly ConcurrentDictionary<ByteString, TransactionState> _active = new();

    public ByteString BeginTransaction(TransactionOptions? options)
    {
        CleanupExpired();

        var mode = options?.ModeCase ?? TransactionOptions.ModeOneofCase.ReadWrite;
        var id = ByteString.New();
        var state = new TransactionState(id, mode, DateTimeOffset.UtcNow);

        _active[id] = state;
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

    public void Complete(ByteString transactionId)
    {
        _active.TryRemove(transactionId, out _);
    }

    /// <summary>
    /// Validates the transaction exists and is not expired, then removes it.
    /// Used by Rollback.
    /// </summary>
    public void ValidateAndComplete(ByteString transactionId)
    {
        GetTransaction(transactionId);
        _active.TryRemove(transactionId, out _);
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
