using FirestoreSharp.Tests.Unit.Builders;
using Google.Cloud.Firestore.V1;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FirestoreSharp.Tests.Unit;

public sealed class FirestoreServiceCollectionTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly Firestore.FirestoreClient _client;

    public FirestoreServiceCollectionTests(WebApplicationFactory<Program> factory)
    {
        var httpClient = factory.CreateDefaultClient();
        _channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });
        _client = new Firestore.FirestoreClient(_channel);
    }

    public void Dispose() => _channel.Dispose();

    [Fact]
    public async Task ListCollectionIds_ReturnsTopLevelCollections()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var colA = $"lcids-col-a-{suffix}";
        var colB = $"lcids-col-b-{suffix}";

        var builderA = new DocumentBuilder().WithCollection(colA).WithId("doc1");
        var builderB = new DocumentBuilder().WithCollection(colB).WithId("doc1");
        await _client.CreateDocumentAsync(builderA.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builderB.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var response = await _client.ListCollectionIdsAsync(
            builderA.BuildListCollectionIdsRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(colA, response.CollectionIds);
        Assert.Contains(colB, response.CollectionIds);
    }

    [Fact]
    public async Task ListCollectionIds_DeduplicatesCollectionIds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"lcids-dedup-{suffix}";

        // Multiple docs in the same collection — should appear only once
        await _client.CreateDocumentAsync(new DocumentBuilder().WithCollection(col).WithId("doc1").BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(new DocumentBuilder().WithCollection(col).WithId("doc2").BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(new DocumentBuilder().WithCollection(col).WithId("doc3").BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var response = await _client.ListCollectionIdsAsync(
            new DocumentBuilder().BuildListCollectionIdsRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, response.CollectionIds.Count(id => id == col));
    }

    [Fact]
    public async Task ListCollectionIds_ReturnsSubcollections_UnderDocument()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var parentCol = $"lcids-parent-{suffix}";
        var parentDoc = "parent-doc";
        var subcolA = $"sub-a-{suffix}";
        var subcolB = $"sub-b-{suffix}";

        // Create parent doc
        var parentBuilder = new DocumentBuilder().WithCollection(parentCol).WithId(parentDoc);
        await _client.CreateDocumentAsync(parentBuilder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        // Create subcollection docs under the parent document
        var subParent = parentBuilder.ExpectedName;
        await _client.CreateDocumentAsync(new DocumentBuilder().WithParent(subParent).WithCollection(subcolA).WithId("doc1").BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(new DocumentBuilder().WithParent(subParent).WithCollection(subcolB).WithId("doc1").BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var response = await _client.ListCollectionIdsAsync(
            parentBuilder.BuildListCollectionIdsRequest(parent: subParent),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(subcolA, response.CollectionIds);
        Assert.Contains(subcolB, response.CollectionIds);
        // Should not include collections from sibling documents
        Assert.DoesNotContain(parentCol, response.CollectionIds);
    }

    [Fact]
    public async Task ListCollectionIds_EmptyParent_ReturnsEmpty()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"lcids-empty-{suffix}";
        var docId = $"lone-doc-{suffix}";

        var builder = new DocumentBuilder().WithCollection(col).WithId(docId);
        await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var response = await _client.ListCollectionIdsAsync(
            builder.BuildListCollectionIdsRequest(parent: builder.ExpectedName),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(response.CollectionIds);
        Assert.Equal("", response.NextPageToken);
    }

    [Fact]
    public async Task ListCollectionIds_WithPageSize_PaginatesResults()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        for (var i = 1; i <= 3; i++)
        {
            await _client.CreateDocumentAsync(
                new DocumentBuilder().WithCollection($"lcids-{i:D2}-{suffix}").WithId("doc1").BuildCreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var page1 = await _client.ListCollectionIdsAsync(
            new DocumentBuilder().BuildListCollectionIdsRequest(pageSize: 2),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, page1.CollectionIds.Count);
        Assert.Collection(page1.CollectionIds.OrderBy(i => i), id => Assert.StartsWith("lcids-01", id), id => Assert.StartsWith("lcids-02", id));
        Assert.NotEmpty(page1.NextPageToken);

        var page2 = await _client.ListCollectionIdsAsync(
            new DocumentBuilder().BuildListCollectionIdsRequest(pageSize: 2, pageToken: page1.NextPageToken),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(page1.CollectionIds.Intersect(page2.CollectionIds));
    }
}
