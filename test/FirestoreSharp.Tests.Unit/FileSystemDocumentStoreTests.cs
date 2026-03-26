using FirestoreSharp.Core;
using FirestoreSharp.Storage.FileSystem;
using FirestoreSharp.Tests.Unit.Builders;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Xunit;

namespace FirestoreSharp.Tests.Unit;

public sealed class FileSystemDocumentStoreTests : IDisposable
{
    private readonly string _basePath = Path.Combine(Path.GetTempPath(), "FirestoreSharpTests", Guid.NewGuid().ToString());

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }
    }

    private FileSystemDocumentStore CreateStore(bool compress = false)
    {
        var options = Options.Create(new FileSystemStorageOptions
        {
            BasePath = _basePath,
            CompressDocuments = compress
        });
        return new FileSystemDocumentStore(options);
    }

    [Fact]
    public async Task CreateAsync_WritesFileToDisk()
    {
        var store = CreateStore();
        var builder = new DocumentBuilder().WithCollection("users").WithId("u1");

        await store.CreateAsync(builder.BuildPath(), builder.Build(), TestContext.Current.CancellationToken);

        var expectedPath = Path.Combine(_basePath, "test-project", "(default)", "users", "u1.bin");
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task GetAsync_ReturnsCreatedDocument()
    {
        var store = CreateStore();
        var builder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("u2")
            .WithField("email", "bob@test.com");

        var path = builder.BuildPath();
        await store.CreateAsync(path, builder.Build(), TestContext.Current.CancellationToken);
        var result = await store.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(builder.ExpectedName, result.Name);
        Assert.Equal("bob@test.com", result.Fields["email"].StringValue);
    }

    [Fact]
    public async Task GetAsync_NotFound_ThrowsRpcException()
    {
        var store = CreateStore();
        var path = new DocumentBuilder().WithCollection("users").WithId("missing").BuildPath();

        var ex = await Assert.ThrowsAsync<RpcException>(() => store.GetAsync(path, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_Duplicate_ThrowsAlreadyExists()
    {
        var store = CreateStore();
        var builder = new DocumentBuilder().WithCollection("users").WithId("u3");
        var path = builder.BuildPath();
        var doc = builder.Build();

        await store.CreateAsync(path, doc, TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<RpcException>(() => store.CreateAsync(path, doc, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.AlreadyExists, ex.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_Compressed_WritesGzFile()
    {
        var store = CreateStore(compress: true);
        var builder = new DocumentBuilder().WithCollection("users").WithId("u4");

        await store.CreateAsync(builder.BuildPath(), builder.Build(), TestContext.Current.CancellationToken);

        var expectedPath = Path.Combine(_basePath, "test-project", "(default)", "users", "u4.bin.gz");
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task Compressed_RoundTrip_PreservesDocument()
    {
        var store = CreateStore(compress: true);
        var builder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("u5")
            .WithField("name", "Charlie");

        var path = builder.BuildPath();
        await store.CreateAsync(path, builder.Build(), TestContext.Current.CancellationToken);
        var result = await store.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(builder.ExpectedName, result.Name);
        Assert.Equal("Charlie", result.Fields["name"].StringValue);
    }

    [Fact]
    public async Task CreateAsync_SubCollection_CreatesNestedPath()
    {
        var store = CreateStore();
        var builder = new DocumentBuilder()
            .WithParent("projects/test-project/databases/(default)/documents/users/u1")
            .WithCollection("posts")
            .WithId("post1")
            .WithField("title", "Hello");

        var path = builder.BuildPath();
        await store.CreateAsync(path, builder.Build(), TestContext.Current.CancellationToken);

        var expectedPath = Path.Combine(_basePath, "test-project", "(default)", "users", "u1", "posts", "post1.bin");
        Assert.True(File.Exists(expectedPath));

        var result = await store.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal("Hello", result.Fields["title"].StringValue);
    }
}
