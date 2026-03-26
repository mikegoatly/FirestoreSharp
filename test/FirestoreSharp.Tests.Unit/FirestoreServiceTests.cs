using FirestoreSharp.Tests.Unit.Builders;
using Google.Cloud.Firestore.V1;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FirestoreSharp.Tests.Unit;

public sealed class FirestoreServiceTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly Firestore.FirestoreClient _client;

    public FirestoreServiceTests(WebApplicationFactory<Program> factory)
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
    public async Task CreateDocument_ReturnsDocumentWithNameAndTimestamps()
    {
        var builder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("user1")
            .WithField("displayName", "Alice");

        var response = await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(builder.ExpectedName, response.Name);
        Assert.NotNull(response.CreateTime);
        Assert.NotNull(response.UpdateTime);
        Assert.Equal("Alice", response.Fields["displayName"].StringValue);
    }

    [Fact]
    public async Task GetDocument_ReturnsCreatedDocument()
    {
        var builder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("user-get-test")
            .WithField("email", "bob@example.com");

        await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var response = await _client.GetDocumentAsync(builder.BuildGetRequest(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(builder.ExpectedName, response.Name);
        Assert.Equal("bob@example.com", response.Fields["email"].StringValue);
    }

    [Fact]
    public async Task GetDocument_NotFound_ThrowsRpcException()
    {
        var builder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("nonexistent");

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            _client.GetDocumentAsync(builder.BuildGetRequest(), cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task CreateDocument_Duplicate_ThrowsAlreadyExists()
    {
        var builder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("user-dup-test")
            .WithField("name", "Charlie");

        await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        Assert.Equal(StatusCode.AlreadyExists, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateDocument_ReplacesAllFields()
    {
        var createBuilder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("user-update-all")
            .WithField("name", "Alice")
            .WithField("email", "alice@example.com");

        var created = await _client.CreateDocumentAsync(createBuilder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var updateBuilder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("user-update-all")
            .WithField("name", "Bob");

        var response = await _client.UpdateDocumentAsync(updateBuilder.BuildUpdateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(createBuilder.ExpectedName, response.Name);
        Assert.Equal("Bob", response.Fields["name"].StringValue);
        Assert.False(response.Fields.ContainsKey("email"));
        Assert.Equal(created.CreateTime, response.CreateTime);
        Assert.True(response.UpdateTime >= created.UpdateTime);
    }

    [Fact]
    public async Task UpdateDocument_WithMask_UpdatesOnlySpecifiedFields()
    {
        var createBuilder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("user-update-mask")
            .WithField("name", "Alice")
            .WithField("email", "alice@example.com");

        await _client.CreateDocumentAsync(createBuilder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var updateBuilder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("user-update-mask")
            .WithField("email", "newemail@example.com");

        var response = await _client.UpdateDocumentAsync(updateBuilder.BuildUpdateRequest("email"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Alice", response.Fields["name"].StringValue);
        Assert.Equal("newemail@example.com", response.Fields["email"].StringValue);
    }

    [Fact]
    public async Task UpdateDocument_WithMask_RemovesFieldNotInInput()
    {
        var createBuilder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("user-update-remove")
            .WithField("name", "Alice")
            .WithField("email", "alice@example.com");

        await _client.CreateDocumentAsync(createBuilder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        // Update mask references "email" but the document doesn't include it → should be removed
        var updateBuilder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("user-update-remove")
            .WithField("name", "Alice");

        var response = await _client.UpdateDocumentAsync(updateBuilder.BuildUpdateRequest("email"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Alice", response.Fields["name"].StringValue);
        Assert.False(response.Fields.ContainsKey("email"));
    }

    [Fact]
    public async Task UpdateDocument_NotFound_ThrowsRpcException()
    {
        var builder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("nonexistent-update");

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            _client.UpdateDocumentAsync(builder.BuildUpdateRequest(), cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateDocument_WithNestedFieldMask_UpdatesNestedField()
    {
        var createBuilder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("user-nested-mask")
            .WithField("name", "Alice")
            .WithField("address.city", "London")
            .WithField("address.zip", "SW1");

        await _client.CreateDocumentAsync(createBuilder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var updateBuilder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("user-nested-mask")
            .WithField("address.city", "Paris");

        var response = await _client.UpdateDocumentAsync(updateBuilder.BuildUpdateRequest("address.city"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Alice", response.Fields["name"].StringValue);
        Assert.Equal("Paris", response.Fields["address"].MapValue.Fields["city"].StringValue);
        Assert.Equal("SW1", response.Fields["address"].MapValue.Fields["zip"].StringValue);
    }

    [Fact]
    public async Task DeleteDocument_RemovesDocument()
    {
        var builder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("user-delete-test")
            .WithField("name", "Alice");

        await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        await _client.DeleteDocumentAsync(builder.BuildDeleteRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            _client.GetDocumentAsync(builder.BuildGetRequest(), cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task DeleteDocument_NotFound_ThrowsRpcException()
    {
        var builder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("nonexistent-delete");

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            _client.DeleteDocumentAsync(builder.BuildDeleteRequest(), cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task BatchGetDocuments_ReturnsFoundAndMissing()
    {
        var builder1 = new DocumentBuilder()
            .WithCollection("users")
            .WithId("batch-found")
            .WithField("name", "Alice");

        await _client.CreateDocumentAsync(builder1.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var missingName = "projects/test-project/databases/(default)/documents/users/batch-missing";

        var request = new BatchGetDocumentsRequest
        {
            Database = "projects/test-project/databases/(default)"
        };
        request.Documents.Add(builder1.ExpectedName);
        request.Documents.Add(missingName);

        var responses = new List<BatchGetDocumentsResponse>();
        using var call = _client.BatchGetDocuments(request, cancellationToken: TestContext.Current.CancellationToken);
        await foreach (var response in call.ResponseStream.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            responses.Add(response);
        }

        Assert.Equal(2, responses.Count);

        var found = Assert.Single(responses, r => r.ResultCase == BatchGetDocumentsResponse.ResultOneofCase.Found);
        Assert.Equal(builder1.ExpectedName, found.Found.Name);
        Assert.Equal("Alice", found.Found.Fields["name"].StringValue);
        Assert.NotNull(found.ReadTime);

        var missing = Assert.Single(responses, r => r.ResultCase == BatchGetDocumentsResponse.ResultOneofCase.Missing);
        Assert.Equal(missingName, missing.Missing);
        Assert.NotNull(missing.ReadTime);
    }

    [Fact]
    public async Task BatchGetDocuments_AllFound_ReturnsAllDocuments()
    {
        var builder1 = new DocumentBuilder()
            .WithCollection("users")
            .WithId("batch-all-1")
            .WithField("name", "Alice");
        var builder2 = new DocumentBuilder()
            .WithCollection("users")
            .WithId("batch-all-2")
            .WithField("name", "Bob");

        await _client.CreateDocumentAsync(builder1.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder2.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new BatchGetDocumentsRequest
        {
            Database = "projects/test-project/databases/(default)"
        };
        request.Documents.Add(builder1.ExpectedName);
        request.Documents.Add(builder2.ExpectedName);

        var responses = new List<BatchGetDocumentsResponse>();
        using var call = _client.BatchGetDocuments(request, cancellationToken: TestContext.Current.CancellationToken);
        await foreach (var response in call.ResponseStream.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            responses.Add(response);
        }

        Assert.Equal(2, responses.Count);
        Assert.All(responses, r => Assert.Equal(BatchGetDocumentsResponse.ResultOneofCase.Found, r.ResultCase));
    }

    [Fact]
    public async Task BatchGetDocuments_AllMissing_ReturnsAllMissing()
    {
        var request = new BatchGetDocumentsRequest
        {
            Database = "projects/test-project/databases/(default)"
        };
        request.Documents.Add("projects/test-project/databases/(default)/documents/users/batch-miss-1");
        request.Documents.Add("projects/test-project/databases/(default)/documents/users/batch-miss-2");

        var responses = new List<BatchGetDocumentsResponse>();
        using var call = _client.BatchGetDocuments(request, cancellationToken: TestContext.Current.CancellationToken);
        await foreach (var response in call.ResponseStream.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            responses.Add(response);
        }

        Assert.Equal(2, responses.Count);
        Assert.All(responses, r => Assert.Equal(BatchGetDocumentsResponse.ResultOneofCase.Missing, r.ResultCase));
    }

    [Fact]
    public async Task BatchGetDocuments_EmptyRequest_ReturnsNoResponses()
    {
        var request = new BatchGetDocumentsRequest
        {
            Database = "projects/test-project/databases/(default)"
        };

        var responses = new List<BatchGetDocumentsResponse>();
        using var call = _client.BatchGetDocuments(request, cancellationToken: TestContext.Current.CancellationToken);
        await foreach (var response in call.ResponseStream.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            responses.Add(response);
        }

        Assert.Empty(responses);
    }
}
