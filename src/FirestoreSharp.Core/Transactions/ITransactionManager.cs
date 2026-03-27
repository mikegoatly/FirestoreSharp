using Google.Cloud.Firestore.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace FirestoreSharp.Core.Transactions
{
    public interface ITransactionManager
    {
        ByteString BeginTransaction(TransactionOptions? options);
        void Complete(ByteString transactionId);
        IReadOnlyDictionary<string, Timestamp?> GetReadSet(ByteString transactionId);
        void RecordRead(ByteString transactionId, string documentResourceName, Timestamp? updateTime);
        void ValidateAndComplete(ByteString transactionId);
        void ValidateCanWrite(ByteString transactionId);
    }
}