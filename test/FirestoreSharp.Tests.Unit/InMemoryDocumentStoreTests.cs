using FirestoreSharp.Core.Stores.InMemory;
using FirestoreSharp.Tests.Unit.Builders;

using Xunit;

namespace FirestoreSharp.Tests.Unit;

public sealed class InMemoryDocumentStoreTests
{
    private static InMemoryDocumentStore CreateStore() => new();

    [Fact]
    public async Task GetKnownDatabasesAsync_EmptyStore_ReturnsEmpty()
    {
        var store = CreateStore();

        var result = await store.GetKnownDatabasesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetKnownDatabasesAsync_SingleDatabase_ReturnsThatDatabase()
    {
        var store = CreateStore();
        var builder = new DocumentBuilder().WithCollection("users").WithId("u1");
        await store.CreateAsync(builder.BuildPath(), builder.Build(), TestContext.Current.CancellationToken);

        var result = await store.GetKnownDatabasesAsync(TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("test-project", result[0].Project);
        Assert.Equal("(default)", result[0].Database);
    }

    [Fact]
    public async Task GetKnownDatabasesAsync_MultipleDatabases_ReturnsAllDistinct()
    {
        var store = CreateStore();
        var b1 = new DocumentBuilder()
            .WithParent("projects/proj-a/databases/db1/documents")
            .WithCollection("col").WithId("d1");
        var b2 = new DocumentBuilder()
            .WithParent("projects/proj-a/databases/db2/documents")
            .WithCollection("col").WithId("d2");
        var b3 = new DocumentBuilder()
            .WithParent("projects/proj-b/databases/db1/documents")
            .WithCollection("col").WithId("d3");

        await store.CreateAsync(b1.BuildPath(), b1.Build(), TestContext.Current.CancellationToken);
        await store.CreateAsync(b2.BuildPath(), b2.Build(), TestContext.Current.CancellationToken);
        await store.CreateAsync(b3.BuildPath(), b3.Build(), TestContext.Current.CancellationToken);

        var result = await store.GetKnownDatabasesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Count);
        Assert.Contains(("proj-a", "db1"), result);
        Assert.Contains(("proj-a", "db2"), result);
        Assert.Contains(("proj-b", "db1"), result);
    }

    [Fact]
    public async Task GetKnownDatabasesAsync_MultipleDocumentsInSameDatabase_ReturnsDatabaseOnce()
    {
        var store = CreateStore();
        var b1 = new DocumentBuilder().WithCollection("users").WithId("u1");
        var b2 = new DocumentBuilder().WithCollection("users").WithId("u2");
        var b3 = new DocumentBuilder().WithCollection("orders").WithId("o1");

        await store.CreateAsync(b1.BuildPath(), b1.Build(), TestContext.Current.CancellationToken);
        await store.CreateAsync(b2.BuildPath(), b2.Build(), TestContext.Current.CancellationToken);
        await store.CreateAsync(b3.BuildPath(), b3.Build(), TestContext.Current.CancellationToken);

        var result = await store.GetKnownDatabasesAsync(TestContext.Current.CancellationToken);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetKnownDatabasesAsync_ResultsAreSorted()
    {
        var store = CreateStore();
        var b1 = new DocumentBuilder()
            .WithParent("projects/proj-z/databases/alpha/documents")
            .WithCollection("col").WithId("d1");
        var b2 = new DocumentBuilder()
            .WithParent("projects/proj-a/databases/zeta/documents")
            .WithCollection("col").WithId("d2");
        var b3 = new DocumentBuilder()
            .WithParent("projects/proj-a/databases/alpha/documents")
            .WithCollection("col").WithId("d3");

        await store.CreateAsync(b1.BuildPath(), b1.Build(), TestContext.Current.CancellationToken);
        await store.CreateAsync(b2.BuildPath(), b2.Build(), TestContext.Current.CancellationToken);
        await store.CreateAsync(b3.BuildPath(), b3.Build(), TestContext.Current.CancellationToken);

        var result = await store.GetKnownDatabasesAsync(TestContext.Current.CancellationToken);

        Assert.Equal([("proj-a", "alpha"), ("proj-a", "zeta"), ("proj-z", "alpha")], result);
    }
}
