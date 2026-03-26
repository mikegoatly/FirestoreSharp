namespace FirestoreSharp.Storage.FileSystem;

public sealed class FileSystemStorageOptions
{
    public required string BasePath { get; set; }

    public bool CompressDocuments { get; set; }
}
