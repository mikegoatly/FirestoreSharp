using System.IO.Compression;
using System.Runtime.CompilerServices;

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

    public async Task CreateAsync(DocumentPath path, Document document, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(path);

        if (File.Exists(filePath))
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, $"Document already exists: {path.ResourceName}"));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await WriteDocumentAsync(filePath, document, FileMode.CreateNew, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Document> GetAsync(DocumentPath path, CancellationToken cancellationToken = default)
    {
        var filePath = GetExistingFilePath(path);

        return await ReadDocumentAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Document?> TryGetAsync(DocumentPath path, CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(path);

        if (!File.Exists(filePath))
        {
            return null;
        }

        return await ReadDocumentAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<Document> ListAsync(ReadOnlyMemory<char> parentPrefix, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (listDir, searchOption) = GetListDirectory(parentPrefix);

        if (!Directory.Exists(listDir))
        {
            yield break;
        }

        var extension = _compressDocuments ? ".bin.gz" : ".bin";
        var files = Directory.EnumerateFiles(listDir, "*" + extension, searchOption)
            .Order(StringComparer.Ordinal);

        foreach (var file in files)
        {
            yield return await ReadDocumentAsync(file, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<Document> UpdateAsync(DocumentPath path, Document document, CancellationToken cancellationToken = default)
    {
        var filePath = GetExistingFilePath(path);

        await WriteDocumentAsync(filePath, document, FileMode.Create, cancellationToken).ConfigureAwait(false);

        return document.Clone();
    }

    public Task DeleteAsync(DocumentPath path, CancellationToken cancellationToken = default)
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

    private string GetExistingFilePath(DocumentPath path)
    {
        var filePath = GetFilePath(path);

        if (!File.Exists(filePath))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Document not found: {path.ResourceName}"));
        }

        return filePath;
    }

    private string GetFilePath(DocumentPath path)
    {
        var relativePath = Path.Combine(path.ToStorageSegments());
        var extension = _compressDocuments ? ".bin.gz" : ".bin";

        return Path.Combine(_basePath, relativePath + extension);
    }

    private (string Directory, SearchOption SearchOption) GetListDirectory(ReadOnlyMemory<char> parentPrefix)
    {
        // Callers append a trailing '/' for prefix-matching purposes; strip it for path-type checks.
        var path = parentPrefix.TrimEnd('/');

        // Database root or document path (query parent): enumerate all descendants so
        // QueryEngine receives all candidates regardless of AllDescendants flag.
        if (DatabasePath.IsDatabaseRoot(path, out var db))
        {
            return (Path.Combine(_basePath, db.Project, db.Database), SearchOption.AllDirectories);
        }

        if (DocumentPath.TryParse(path) is { } docPath)
        {
            var docDir = Path.Combine(_basePath, Path.Combine(docPath.ToStorageSegments()));
            return (docDir, SearchOption.AllDirectories);
        }

        // Collection path (ListDocuments): only direct children of the collection.
        var collection = CollectionPath.Parse(path);
        return (Path.Combine(_basePath, Path.Combine(collection.ToStorageSegments())), SearchOption.TopDirectoryOnly);
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
