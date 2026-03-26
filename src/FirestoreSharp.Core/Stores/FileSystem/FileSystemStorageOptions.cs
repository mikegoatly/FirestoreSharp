namespace FirestoreSharp.Core.Stores.FileSystem;

public sealed class FileSystemStorageOptions
{
    public required string BasePath { get; set; }

    public bool CompressDocuments { get; set; }
}
