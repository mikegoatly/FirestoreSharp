using FirestoreSharp.Tests.Unit.Builders;
using Google.Cloud.Firestore.V1;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

using Value = Google.Cloud.Firestore.V1.Value;

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

    [Fact]
    public async Task ListDocuments_ReturnsDocumentsInCollection()
    {
        var builder1 = new DocumentBuilder()
            .WithCollection("items")
            .WithId("list-item-1")
            .WithField("name", "First");
        var builder2 = new DocumentBuilder()
            .WithCollection("items")
            .WithId("list-item-2")
            .WithField("name", "Second");

        await _client.CreateDocumentAsync(builder1.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder2.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var response = await _client.ListDocumentsAsync(
            new DocumentBuilder().WithCollection("items").BuildListRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        var names = response.Documents.Select(d => d.Name).ToList();
        Assert.Contains(builder1.ExpectedName, names);
        Assert.Contains(builder2.ExpectedName, names);
    }

    [Fact]
    public async Task ListDocuments_EmptyCollection_ReturnsEmpty()
    {
        var response = await _client.ListDocumentsAsync(
            new DocumentBuilder().WithCollection("empty-collection").BuildListRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(response.Documents);
        Assert.Equal("", response.NextPageToken);
    }

    [Fact]
    public async Task ListDocuments_WithPageSize_ReturnsPagedResults()
    {
        for (var i = 0; i < 3; i++)
        {
            var builder = new DocumentBuilder()
                .WithCollection("paged")
                .WithId($"page-doc-{i:D2}")
                .WithField("index", i);
            await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        }

        var page1 = await _client.ListDocumentsAsync(
            new DocumentBuilder().WithCollection("paged").BuildListRequest(pageSize: 2),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, page1.Documents.Count);
        Assert.NotEmpty(page1.NextPageToken);

        var page2 = await _client.ListDocumentsAsync(
            new DocumentBuilder().WithCollection("paged").BuildListRequest(pageSize: 2, pageToken: page1.NextPageToken),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(page2.Documents);
        Assert.Equal("", page2.NextPageToken);

        // No overlap between pages
        var allNames = page1.Documents.Concat(page2.Documents).Select(d => d.Name).ToList();
        Assert.Equal(3, allNames.Distinct().Count());
    }

    [Fact]
    public async Task ListDocuments_DoesNotReturnDocumentsFromOtherCollections()
    {
        var inScope = new DocumentBuilder()
            .WithCollection("scoped")
            .WithId("scoped-doc")
            .WithField("val", "yes");
        var outOfScope = new DocumentBuilder()
            .WithCollection("other")
            .WithId("other-doc")
            .WithField("val", "no");

        await _client.CreateDocumentAsync(inScope.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(outOfScope.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var response = await _client.ListDocumentsAsync(
            new DocumentBuilder().WithCollection("scoped").BuildListRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(response.Documents, d => Assert.StartsWith(
            "projects/test-project/databases/(default)/documents/scoped/", d.Name));
    }

    // ── RunQuery ──────────────────────────────────────────────────────────

    private static async Task<List<RunQueryResponse>> RunQueryAsync(
        Firestore.FirestoreClient client,
        RunQueryRequest request,
        CancellationToken cancellationToken)
    {
        var responses = new List<RunQueryResponse>();
        using var call = client.RunQuery(request, cancellationToken: cancellationToken);
        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            responses.Add(response);
        }
        return responses;
    }

    [Fact]
    public async Task RunQuery_NoFilter_ReturnsAllDocumentsInCollection()
    {
        var alice = new DocumentBuilder().WithCollection("rq-nofilter").WithId("alice").WithField("name", "Alice");
        var bob = new DocumentBuilder().WithCollection("rq-nofilter").WithId("bob").WithField("name", "Bob");

        await _client.CreateDocumentAsync(alice.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(bob.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new DocumentBuilder().WithCollection("rq-nofilter").BuildRunQueryRequest();
        var responses = await RunQueryAsync(_client, request, TestContext.Current.CancellationToken);

        // Final done=true response is included; filter it out to count real documents
        var docResponses = responses.Where(r => r.Document is not null).ToList();
        Assert.Equal(2, docResponses.Count);
        Assert.Contains(docResponses, r => r.Document.Name == alice.ExpectedName);
        Assert.Contains(docResponses, r => r.Document.Name == bob.ExpectedName);
    }

    [Fact]
    public async Task RunQuery_EqualityFilter_ReturnsMatchingDocuments()
    {
        var active1 = new DocumentBuilder().WithCollection("rq-status").WithId("active-1").WithField("status", "active");
        var active2 = new DocumentBuilder().WithCollection("rq-status").WithId("active-2").WithField("status", "active");
        var inactive = new DocumentBuilder().WithCollection("rq-status").WithId("inactive-1").WithField("status", "inactive");

        await _client.CreateDocumentAsync(active1.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(active2.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(inactive.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new DocumentBuilder().WithCollection("rq-status").BuildRunQueryRequest(query =>
        {
            query.Where = new StructuredQuery.Types.Filter
            {
                FieldFilter = new StructuredQuery.Types.FieldFilter
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "status" },
                    Op = StructuredQuery.Types.FieldFilter.Types.Operator.Equal,
                    Value = new Value { StringValue = "active" }
                }
            };
        });

        var responses = await RunQueryAsync(_client, request, TestContext.Current.CancellationToken);
        var docs = responses.Where(r => r.Document is not null).ToList();

        Assert.Equal(2, docs.Count);
        Assert.All(docs, r => Assert.Equal("active", r.Document.Fields["status"].StringValue));
    }

    [Fact]
    public async Task RunQuery_InequalityFilter_ReturnsMatchingDocuments()
    {
        var u1 = new DocumentBuilder().WithCollection("rq-ineq").WithId("u1").WithField("score", 10L);
        var u2 = new DocumentBuilder().WithCollection("rq-ineq").WithId("u2").WithField("score", 20L);
        var u3 = new DocumentBuilder().WithCollection("rq-ineq").WithId("u3").WithField("score", 30L);

        await _client.CreateDocumentAsync(u1.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(u2.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(u3.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new DocumentBuilder().WithCollection("rq-ineq").BuildRunQueryRequest(query =>
        {
            query.Where = new StructuredQuery.Types.Filter
            {
                FieldFilter = new StructuredQuery.Types.FieldFilter
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" },
                    Op = StructuredQuery.Types.FieldFilter.Types.Operator.GreaterThan,
                    Value = new Value { IntegerValue = 15 }
                }
            };
        });

        var responses = await RunQueryAsync(_client, request, TestContext.Current.CancellationToken);
        var docs = responses.Where(r => r.Document is not null).ToList();

        Assert.Equal(2, docs.Count);
        Assert.All(docs, r => Assert.True(r.Document.Fields["score"].IntegerValue > 15));
    }

    [Fact]
    public async Task RunQuery_OrderBy_ReturnsSortedResults()
    {
        var u3 = new DocumentBuilder().WithCollection("rq-order").WithId("u3").WithField("rank", 3L);
        var u1 = new DocumentBuilder().WithCollection("rq-order").WithId("u1").WithField("rank", 1L);
        var u2 = new DocumentBuilder().WithCollection("rq-order").WithId("u2").WithField("rank", 2L);

        await _client.CreateDocumentAsync(u3.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(u1.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(u2.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new DocumentBuilder().WithCollection("rq-order").BuildRunQueryRequest(query =>
        {
            query.OrderBy.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = "rank" },
                Direction = StructuredQuery.Types.Direction.Ascending
            });
        });

        var responses = await RunQueryAsync(_client, request, TestContext.Current.CancellationToken);
        var docs = responses.Where(r => r.Document is not null).ToList();

        Assert.Equal(3, docs.Count);
        Assert.Equal(1L, docs[0].Document.Fields["rank"].IntegerValue);
        Assert.Equal(2L, docs[1].Document.Fields["rank"].IntegerValue);
        Assert.Equal(3L, docs[2].Document.Fields["rank"].IntegerValue);
    }

    [Fact]
    public async Task RunQuery_Limit_CapsResults()
    {
        for (var i = 1; i <= 5; i++)
        {
            var doc = new DocumentBuilder().WithCollection("rq-limit").WithId($"d{i:D2}").WithField("n", (long)i);
            await _client.CreateDocumentAsync(doc.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        }

        var request = new DocumentBuilder().WithCollection("rq-limit").BuildRunQueryRequest(query =>
        {
            query.OrderBy.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = "n" },
                Direction = StructuredQuery.Types.Direction.Ascending
            });
            query.Limit = 3;
        });

        var docs = (await RunQueryAsync(_client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).ToList();

        Assert.Equal(3, docs.Count);
        Assert.Equal(1L, docs[0].Document.Fields["n"].IntegerValue);
    }

    [Fact]
    public async Task RunQuery_OffsetAndLimit_PagesResults()
    {
        for (var i = 1; i <= 5; i++)
        {
            var doc = new DocumentBuilder().WithCollection("rq-page").WithId($"p{i:D2}").WithField("n", (long)i);
            await _client.CreateDocumentAsync(doc.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        }

        var request = new DocumentBuilder().WithCollection("rq-page").BuildRunQueryRequest(query =>
        {
            query.OrderBy.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = "n" },
                Direction = StructuredQuery.Types.Direction.Ascending
            });
            query.Offset = 2;
            query.Limit = 2;
        });

        var docs = (await RunQueryAsync(_client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).ToList();

        Assert.Equal(2, docs.Count);
        Assert.Equal(3L, docs[0].Document.Fields["n"].IntegerValue);
        Assert.Equal(4L, docs[1].Document.Fields["n"].IntegerValue);
    }

    [Fact]
    public async Task RunQuery_Select_ReturnsOnlySpecifiedFields()
    {
        var doc = new DocumentBuilder()
            .WithCollection("rq-select").WithId("s1")
            .WithField("name", "Alice")
            .WithField("email", "alice@example.com")
            .WithField("age", 30L);

        await _client.CreateDocumentAsync(doc.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new DocumentBuilder().WithCollection("rq-select").BuildRunQueryRequest(query =>
        {
            query.Select = new StructuredQuery.Types.Projection();
            query.Select.Fields.Add(new StructuredQuery.Types.FieldReference { FieldPath = "name" });
            query.Select.Fields.Add(new StructuredQuery.Types.FieldReference { FieldPath = "email" });
        });

        var docs = (await RunQueryAsync(_client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).ToList();

        var result = Assert.Single(docs).Document;
        Assert.True(result.Fields.ContainsKey("name"));
        Assert.True(result.Fields.ContainsKey("email"));
        Assert.False(result.Fields.ContainsKey("age"));
    }

    [Fact]
    public async Task RunQuery_NoMatchingDocuments_SendsDoneResponse()
    {
        var request = new DocumentBuilder().WithCollection("rq-empty").BuildRunQueryRequest(query =>
        {
            query.Where = new StructuredQuery.Types.Filter
            {
                FieldFilter = new StructuredQuery.Types.FieldFilter
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "status" },
                    Op = StructuredQuery.Types.FieldFilter.Types.Operator.Equal,
                    Value = new Value { StringValue = "active" }
                }
            };
        });

        var responses = await RunQueryAsync(_client, request, TestContext.Current.CancellationToken);

        Assert.Single(responses); // Only the done=true message
        Assert.True(responses[0].Done);
        Assert.Null(responses[0].Document);
    }

    [Fact]
    public async Task RunQuery_CompositeAndFilter_ReturnsMatchingDocuments()
    {
        var match = new DocumentBuilder().WithCollection("rq-and").WithId("match").WithField("active", true).WithField("score", 50L);
        var noMatch1 = new DocumentBuilder().WithCollection("rq-and").WithId("no-active").WithField("active", false).WithField("score", 50L);
        var noMatch2 = new DocumentBuilder().WithCollection("rq-and").WithId("no-score").WithField("active", true).WithField("score", 5L);

        await _client.CreateDocumentAsync(match.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(noMatch1.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(noMatch2.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new DocumentBuilder().WithCollection("rq-and").BuildRunQueryRequest(query =>
        {
            var composite = new StructuredQuery.Types.CompositeFilter
            {
                Op = StructuredQuery.Types.CompositeFilter.Types.Operator.And
            };
            composite.Filters.Add(new StructuredQuery.Types.Filter
            {
                FieldFilter = new StructuredQuery.Types.FieldFilter
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "active" },
                    Op = StructuredQuery.Types.FieldFilter.Types.Operator.Equal,
                    Value = new Value { BooleanValue = true }
                }
            });
            composite.Filters.Add(new StructuredQuery.Types.Filter
            {
                FieldFilter = new StructuredQuery.Types.FieldFilter
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" },
                    Op = StructuredQuery.Types.FieldFilter.Types.Operator.GreaterThanOrEqual,
                    Value = new Value { IntegerValue = 20 }
                }
            });
            query.Where = new StructuredQuery.Types.Filter { CompositeFilter = composite };
        });

        var docs = (await RunQueryAsync(_client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).ToList();

        Assert.Single(docs);
        Assert.Equal(match.ExpectedName, docs[0].Document.Name);
    }

    [Fact]
    public async Task RunQuery_AllDescendants_ReturnsDocumentsInSubcollections()
    {
        var parent = "projects/test-project/databases/(default)/documents";

        // Create a document in a subcollection: rq-groups/g1/members/m1
        var member = new DocumentBuilder()
            .WithParent($"{parent}/rq-groups/g1")
            .WithCollection("members")
            .WithId("m1")
            .WithField("role", "admin");

        await _client.CreateDocumentAsync(member.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new RunQueryRequest
        {
            Parent = parent,
            StructuredQuery = new StructuredQuery()
        };
        request.StructuredQuery.From.Add(new StructuredQuery.Types.CollectionSelector
        {
            CollectionId = "members",
            AllDescendants = true
        });

        var docs = (await RunQueryAsync(_client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).ToList();

        Assert.Single(docs);
        Assert.Equal(member.ExpectedName, docs[0].Document.Name);
    }

    [Fact]
    public async Task RunQuery_AllDocuments_SendsReadTimeOnEachResult()
    {
        var doc = new DocumentBuilder().WithCollection("rq-readtime").WithId("rt1").WithField("x", "y");
        await _client.CreateDocumentAsync(doc.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new DocumentBuilder().WithCollection("rq-readtime").BuildRunQueryRequest();
        var responses = await RunQueryAsync(_client, request, TestContext.Current.CancellationToken);

        Assert.All(responses, r => Assert.NotNull(r.ReadTime));
    }
}
