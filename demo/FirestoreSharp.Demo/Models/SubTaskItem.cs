using Google.Cloud.Firestore;

namespace FirestoreSharp.Demo.Models;

[FirestoreData]
public sealed class SubTaskItem
{
    [FirestoreDocumentId]
    public string? Id { get; set; }

    [FirestoreProperty("title")]
    public string Title { get; set; } = string.Empty;

    [FirestoreProperty("completed")]
    public bool Completed { get; set; }

    [FirestoreDocumentCreateTimestamp]
    public Google.Cloud.Firestore.Timestamp? CreatedAt { get; set; }
}
