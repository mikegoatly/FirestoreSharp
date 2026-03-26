using System.IO.Compression;
using FirestoreSharp.Core;
using Google.Cloud.Firestore.V1;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace FirestoreSharp.Storage.FileSystem;

public sealed class FileSystemDocumentStore(IOptions<FileSystemStorageOptions> options) : IDocumentStore
{
    private readonly FileSystemStorageOptions _options = options.Value;

    public async Task CreateAsync(FirestorePath path, Document document, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(path);

        if (File.Exists(filePath))
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, $"Document already exists: {path.ResourceName}"));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await WriteDocumentAsync(filePath, document, FileMode.CreateNew, cancellationToken);
    }

    public async Task<Document> GetAsync(FirestorePath path, CancellationToken cancellationToken = default)
    {
        var filePath = GetExistingFilePath(path);

        return await ReadDocumentAsync(filePath, cancellationToken);
    }

    public async Task<Document> UpdateAsync(FirestorePath path, Document document, CancellationToken cancellationToken = default)
    {
        var filePath = GetExistingFilePath(path);

        await WriteDocumentAsync(filePath, document, FileMode.Create, cancellationToken);

        return document.Clone();
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
        var extension = _options.CompressDocuments ? ".bin.gz" : ".bin";

        return Path.Combine(_options.BasePath, relativePath + extension);
    }

    private async Task WriteDocumentAsync(string filePath, Document document, FileMode fileMode, CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(filePath, fileMode, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        if (_options.CompressDocuments)
        {
            await using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
            document.WriteTo(gzipStream);
        }
        else
        {
            document.WriteTo(fileStream);
        }
    }

    private async Task<Document> ReadDocumentAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        if (_options.CompressDocuments)
        {
            await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            return Document.Parser.ParseFrom(gzipStream);
        }
        else
        {
            return Document.Parser.ParseFrom(fileStream);
        }
    }
}
