using System.IO.Compression;

using FirestoreSharp.Core;

using Google.Cloud.Firestore.V1;
using Google.Protobuf;

using Grpc.Core;

using Microsoft.Extensions.Options;

namespace FirestoreSharp.Core.Stores.FileSystem;

internal sealed class FileSystemDocumentStore(IOptions<FileSystemStorageOptions> options) : IDocumentStore
{
    private readonly string _basePath = Path.GetFullPath(options.Value.BasePath);
    private readonly bool _compressDocuments = options.Value.CompressDocuments;

    public async Task CreateAsync(FirestorePath path, Document document, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(path);

        if (File.Exists(filePath))
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, $"Document already exists: {path.ResourceName}"));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await WriteDocumentAsync(filePath, document, FileMode.CreateNew, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Document> GetAsync(FirestorePath path, CancellationToken cancellationToken = default)
    {
        var filePath = GetExistingFilePath(path);

        return await ReadDocumentAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Document> UpdateAsync(FirestorePath path, Document document, CancellationToken cancellationToken = default)
    {
        var filePath = GetExistingFilePath(path);

        await WriteDocumentAsync(filePath, document, FileMode.Create, cancellationToken).ConfigureAwait(false);

        return document.Clone();
    }

    public Task DeleteAsync(FirestorePath path, CancellationToken cancellationToken = default)
    {
        var filePath = GetExistingFilePath(path);

        File.Delete(filePath);

        DeleteEmptyParentDirectories(Path.GetDirectoryName(filePath)!);

        return Task.CompletedTask;
    }

    private void DeleteEmptyParentDirectories(string directory)
    {
        string? currentPath = directory;
        while (!string.IsNullOrEmpty(currentPath)
               && !string.Equals(currentPath, _basePath, StringComparison.Ordinal)
               && Directory.Exists(currentPath)
               && !Directory.EnumerateFileSystemEntries(currentPath).Any())
        {
            Directory.Delete(currentPath);
            currentPath = Path.GetDirectoryName(currentPath);
        }
    }

    private string GetExistingFilePath(FirestorePath path)
    {
        var filePath = GetFilePath(path);

        if (!File.Exists(filePath))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Document not found: {path.ResourceName}"));
        }

        return filePath;
    }

    private string GetFilePath(FirestorePath path)
    {
        var relativePath = Path.Combine(path.ToStorageSegments());
        var extension = _compressDocuments ? ".bin.gz" : ".bin";

        return Path.Combine(_basePath, relativePath + extension);
    }

    private async Task WriteDocumentAsync(string filePath, Document document, FileMode fileMode, CancellationToken cancellationToken)
    {
#pragma warning disable CA2007 // Consider calling ConfigureAwait on the awaited task - false positive
        await using var fileStream = new FileStream(filePath, fileMode, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
#pragma warning restore CA2007 // Consider calling ConfigureAwait on the awaited task
        if (_compressDocuments)
        {
            using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
            document.WriteTo(gzipStream);
        }
        else
        {
            document.WriteTo(fileStream);
        }
    }

    private async Task<Document> ReadDocumentAsync(string filePath, CancellationToken cancellationToken)
    {
#pragma warning disable CA2007 // Consider calling ConfigureAwait on the awaited task - false positive
        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
#pragma warning restore CA2007 // Consider calling ConfigureAwait on the awaited task
        if (_compressDocuments)
        {
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            return Document.Parser.ParseFrom(gzipStream);
        }
        else
        {
            return Document.Parser.ParseFrom(fileStream);
        }
    }
}
