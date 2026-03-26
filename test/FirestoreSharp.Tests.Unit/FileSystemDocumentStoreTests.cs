using FirestoreSharp.Core;
using FirestoreSharp.Core.Stores.FileSystem;
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

    [Fact]
    public async Task UpdateAsync_OverwritesExistingDocument()
    {
        var store = CreateStore();
        var builder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("u-update")
            .WithField("name", "Alice");

        var path = builder.BuildPath();
        await store.CreateAsync(path, builder.Build(), TestContext.Current.CancellationToken);

        var updatedDoc = new DocumentBuilder()
            .WithCollection("users")
            .WithId("u-update")
            .WithField("name", "Bob")
            .Build();

        await store.UpdateAsync(path, updatedDoc, TestContext.Current.CancellationToken);
        var result = await store.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal("Bob", result.Fields["name"].StringValue);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ThrowsRpcException()
    {
        var store = CreateStore();
        var builder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("u-missing");

        var path = builder.BuildPath();

        var ex = await Assert.ThrowsAsync<RpcException>(() => store.UpdateAsync(path, builder.Build(), TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFileFromDisk()
    {
        var store = CreateStore();
        var builder = new DocumentBuilder().WithCollection("users").WithId("u-delete");
        var path = builder.BuildPath();

        await store.CreateAsync(path, builder.Build(), TestContext.Current.CancellationToken);
        await store.DeleteAsync(path, TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<RpcException>(() => store.GetAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_CleansUpEmptyParentDirectories()
    {
        var store = CreateStore();
        var builder = new DocumentBuilder()
            .WithParent("projects/test-project/databases/(default)/documents/users/u1")
            .WithCollection("posts")
            .WithId("post1");

        var path = builder.BuildPath();
        await store.CreateAsync(path, builder.Build(), TestContext.Current.CancellationToken);
        await store.DeleteAsync(path, TestContext.Current.CancellationToken);

        // "posts" folder should be removed since it's now empty
        var postsDir = Path.Combine(_basePath, "test-project", "(default)", "users", "u1", "posts");
        Assert.False(Directory.Exists(postsDir));

        // "u1" folder should also be removed since it's now empty
        var u1Dir = Path.Combine(_basePath, "test-project", "(default)", "users", "u1");
        Assert.False(Directory.Exists(u1Dir));

        // "users" folder should also be removed
        var usersDir = Path.Combine(_basePath, "test-project", "(default)", "users");
        Assert.False(Directory.Exists(usersDir));
    }

    [Fact]
    public async Task DeleteAsync_DoesNotRemoveNonEmptyParentDirectories()
    {
        var store = CreateStore();
        var builder1 = new DocumentBuilder().WithCollection("users").WithId("u-keep");
        var builder2 = new DocumentBuilder().WithCollection("users").WithId("u-remove");

        await store.CreateAsync(builder1.BuildPath(), builder1.Build(), TestContext.Current.CancellationToken);
        await store.CreateAsync(builder2.BuildPath(), builder2.Build(), TestContext.Current.CancellationToken);

        await store.DeleteAsync(builder2.BuildPath(), TestContext.Current.CancellationToken);

        // "users" folder should still exist because u-keep is still there
        var usersDir = Path.Combine(_basePath, "test-project", "(default)", "users");
        Assert.True(Directory.Exists(usersDir));
    }

    [Fact]
    public async Task DeleteAsync_DoesNotDeleteBasePath()
    {
        var store = CreateStore();
        var builder = new DocumentBuilder().WithCollection("users").WithId("u-only");

        var path = builder.BuildPath();
        await store.CreateAsync(path, builder.Build(), TestContext.Current.CancellationToken);
        await store.DeleteAsync(path, TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(_basePath));
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ThrowsRpcException()
    {
        var store = CreateStore();
        var path = new DocumentBuilder().WithCollection("users").WithId("u-missing-delete").BuildPath();

        var ex = await Assert.ThrowsAsync<RpcException>(() => store.DeleteAsync(path, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }
}