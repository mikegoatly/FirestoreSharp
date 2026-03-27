using FirestoreSharp.Tests.Unit.Builders;
using Google.Cloud.Firestore.V1;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FirestoreSharp.Tests.Unit;

public sealed class FirestoreServicePartitionQueryTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly Firestore.FirestoreClient _client;

    public FirestoreServicePartitionQueryTests(WebApplicationFactory<Program> factory)
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
    public async Task PartitionQuery_ReturnsNMinusOneCursors()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"pq-basic-{suffix}";

        for (var i = 1; i <= 10; i++)
        {
            await _client.CreateDocumentAsync(
                new DocumentBuilder().WithCollection(col).WithId($"doc-{i:D2}").BuildCreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // partition_count = 3 → expect 3 split-point cursors (dividing into 4 sub-ranges)
        var response = await _client.PartitionQueryAsync(
            new DocumentBuilder().WithCollection(col).BuildPartitionQueryRequest(partitionCount: 3),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, response.Partitions.Count);
        Assert.Equal("", response.NextPageToken);
    }

    [Fact]
    public async Task PartitionQuery_CursorsAreReferenceValuesWithBeforeTrue()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"pq-cursors-{suffix}";

        for (var i = 1; i <= 6; i++)
        {
            await _client.CreateDocumentAsync(
                new DocumentBuilder().WithCollection(col).WithId($"doc-{i:D2}").BuildCreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var response = await _client.PartitionQueryAsync(
            new DocumentBuilder().WithCollection(col).BuildPartitionQueryRequest(partitionCount: 2),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Partitions.Count);
        foreach (var cursor in response.Partitions)
        {
            Assert.True(cursor.Before);
            Assert.Single(cursor.Values);
            Assert.NotEmpty(cursor.Values[0].ReferenceValue);
        }
    }

    [Fact]
    public async Task PartitionQuery_CursorsAreSortedByName()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"pq-sorted-{suffix}";

        for (var i = 1; i <= 9; i++)
        {
            await _client.CreateDocumentAsync(
                new DocumentBuilder().WithCollection(col).WithId($"doc-{i:D2}").BuildCreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var response = await _client.PartitionQueryAsync(
            new DocumentBuilder().WithCollection(col).BuildPartitionQueryRequest(partitionCount: 4),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(4, response.Partitions.Count);
        var names = response.Partitions.Select(c => c.Values[0].ReferenceValue).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
    }

    [Fact]
    public async Task PartitionQuery_FewerDocsThanPartitions_ReturnsAtMostNumDocsMinusOneCursors()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"pq-few-{suffix}";

        for (var i = 1; i <= 3; i++)
        {
            await _client.CreateDocumentAsync(
                new DocumentBuilder().WithCollection(col).WithId($"doc-{i:D2}").BuildCreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var response = await _client.PartitionQueryAsync(
            new DocumentBuilder().WithCollection(col).BuildPartitionQueryRequest(partitionCount: 10),
            cancellationToken: TestContext.Current.CancellationToken);

        // At most numDocs - 1 = 2 cursors
        Assert.True(response.Partitions.Count <= 2);
        Assert.Equal("", response.NextPageToken);
    }

    [Fact]
    public async Task PartitionQuery_SingleDocument_ReturnsEmptyPartitions()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"pq-single-{suffix}";

        await _client.CreateDocumentAsync(
            new DocumentBuilder().WithCollection(col).WithId("only-doc").BuildCreateRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        var response = await _client.PartitionQueryAsync(
            new DocumentBuilder().WithCollection(col).BuildPartitionQueryRequest(partitionCount: 5),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(response.Partitions);
        Assert.Equal("", response.NextPageToken);
    }

    [Fact]
    public async Task PartitionQuery_WithPageSize_PaginatesCorrectly()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"pq-paged-{suffix}";

        for (var i = 1; i <= 10; i++)
        {
            await _client.CreateDocumentAsync(
                new DocumentBuilder().WithCollection(col).WithId($"doc-{i:D2}").BuildCreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // Request 6 cursors total, 2 per page → 3 pages
        var page1 = await _client.PartitionQueryAsync(
            new DocumentBuilder().WithCollection(col).BuildPartitionQueryRequest(partitionCount: 6, pageSize: 2),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, page1.Partitions.Count);
        Assert.NotEmpty(page1.NextPageToken);

        var page2 = await _client.PartitionQueryAsync(
            new DocumentBuilder().WithCollection(col).BuildPartitionQueryRequest(partitionCount: 6, pageSize: 2, pageToken: page1.NextPageToken),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, page2.Partitions.Count);
        Assert.NotEmpty(page2.NextPageToken);

        var page3 = await _client.PartitionQueryAsync(
            new DocumentBuilder().WithCollection(col).BuildPartitionQueryRequest(partitionCount: 6, pageSize: 2, pageToken: page2.NextPageToken),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, page3.Partitions.Count);
        Assert.Equal("", page3.NextPageToken);

        // All cursor names across pages are distinct and globally ordered
        var allNames = page1.Partitions.Concat(page2.Partitions).Concat(page3.Partitions)
            .Select(c => c.Values[0].ReferenceValue).ToList();
        Assert.Equal(6, allNames.Distinct().Count());
        Assert.Equal(allNames.OrderBy(n => n, StringComparer.Ordinal), allNames);
    }

    [Fact]
    public async Task PartitionQuery_InvalidPartitionCount_ThrowsInvalidArgument()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            _client.PartitionQueryAsync(
                new DocumentBuilder().WithCollection("any").BuildPartitionQueryRequest(partitionCount: 0),
                cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task PartitionQuery_NonCollectionGroupQuery_ReturnsEmpty()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"pq-noncg-{suffix}";

        await _client.CreateDocumentAsync(
            new DocumentBuilder().WithCollection(col).WithId("doc1").BuildCreateRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        var query = new StructuredQuery();
        query.From.Add(new StructuredQuery.Types.CollectionSelector
        {
            CollectionId = col,
            AllDescendants = false
        });

        var request = new PartitionQueryRequest
        {
            Parent = new DocumentBuilder().Parent,
            StructuredQuery = query,
            PartitionCount = 3
        };

        var response = await _client.PartitionQueryAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(response.Partitions);
    }
}
