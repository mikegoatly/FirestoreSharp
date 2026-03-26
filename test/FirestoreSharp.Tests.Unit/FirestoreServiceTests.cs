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

        var response = await _client.CreateDocumentAsync(builder.BuildCreateRequest());

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

        await _client.CreateDocumentAsync(builder.BuildCreateRequest());

        var response = await _client.GetDocumentAsync(builder.BuildGetRequest());

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
            _client.GetDocumentAsync(builder.BuildGetRequest()).ResponseAsync);

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task CreateDocument_Duplicate_ThrowsAlreadyExists()
    {
        var builder = new DocumentBuilder()
            .WithCollection("users")
            .WithId("user-dup-test")
            .WithField("name", "Charlie");

        await _client.CreateDocumentAsync(builder.BuildCreateRequest());

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            _client.CreateDocumentAsync(builder.BuildCreateRequest()).ResponseAsync);

        Assert.Equal(StatusCode.AlreadyExists, ex.StatusCode);
    }
}
