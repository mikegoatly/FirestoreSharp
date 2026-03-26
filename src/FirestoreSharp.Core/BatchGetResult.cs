using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;

namespace FirestoreSharp.Core;

public abstract record BatchGetResult(Timestamp ReadTime);

public sealed record BatchGetFoundResult(Document Document, Timestamp ReadTime) : BatchGetResult(ReadTime);

public sealed record BatchGetMissingResult(string ResourceName, Timestamp ReadTime) : BatchGetResult(ReadTime);
