using FirestoreSharp.Tests.Unit.Builders;
using Google.Cloud.Firestore.V1;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

using Value = Google.Cloud.Firestore.V1.Value;

namespace FirestoreSharp.Tests.Unit;

/// <summary>
/// Integration tests for the Listen RPC — these go through the real gRPC channel and
/// exercise the full stack: FirestoreGrpcService → ListenerService → ListenerConnection,
/// with mutations triggered via other gRPC calls.
/// </summary>
public sealed class ListenGrpcTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string Database = "projects/test-project/databases/(default)";

    private readonly GrpcChannel _channel;
    private readonly Firestore.FirestoreClient _client;

    public ListenGrpcTests(WebApplicationFactory<Program> factory)
    {
        var httpClient = factory.CreateDefaultClient();
        _channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });
        _client = new Firestore.FirestoreClient(_channel);
    }

    public void Dispose() => _channel.Dispose();

    // ── Document target: initial snapshot ──────────────────────────────────

    [Fact]
    public async Task Listen_DocumentTarget_ExistingDocument_InitialSnapshotContainsDocument()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder().WithCollection("listen-init").WithId("existing-1").WithField("x", "a");
        await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: ct);

        using var call = _client.Listen(cancellationToken: ct);
        await call.RequestStream.WriteAsync(AddTargetRequest(BuildDocumentTarget(1, builder.ExpectedName)), ct);

        var add = await ReadNextAsync(call, ct);
        Assert.Equal(TargetChange.Types.TargetChangeType.Add, add.TargetChange.TargetChangeType);
        Assert.Contains(1, add.TargetChange.TargetIds);

        var docChange = await ReadNextAsync(call, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentChange, docChange.ResponseTypeCase);
        Assert.Equal(builder.ExpectedName, docChange.DocumentChange.Document.Name);
        Assert.Equal("a", docChange.DocumentChange.Document.Fields["x"].StringValue);
        Assert.Contains(1, docChange.DocumentChange.TargetIds);

        var current = await ReadNextAsync(call, ct);
        Assert.Equal(TargetChange.Types.TargetChangeType.Current, current.TargetChange.TargetChangeType);

        await call.RequestStream.CompleteAsync();
    }

    [Fact]
    public async Task Listen_DocumentTarget_MissingDocument_InitialSnapshotHasNoDocumentChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var resourceName = $"{Database}/documents/listen-init/missing-1";

        using var call = _client.Listen(cancellationToken: ct);
        await call.RequestStream.WriteAsync(AddTargetRequest(BuildDocumentTarget(1, resourceName)), ct);

        var add = await ReadNextAsync(call, ct);
        Assert.Equal(TargetChange.Types.TargetChangeType.Add, add.TargetChange.TargetChangeType);

        var current = await ReadNextAsync(call, ct);
        Assert.Equal(TargetChange.Types.TargetChangeType.Current, current.TargetChange.TargetChangeType);

        // No DocumentChange should be pending
        await AssertNoMoreResponsesAsync(call, ct);

        await call.RequestStream.CompleteAsync();
    }

    // ── Document target: live notifications ────────────────────────────────

    [Fact]
    public async Task Listen_DocumentTarget_CreateDocument_ReceivesDocumentChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder().WithCollection("listen-live").WithId("create-1").WithField("v", "hello");

        using var call = _client.Listen(cancellationToken: ct);
        await call.RequestStream.WriteAsync(AddTargetRequest(BuildDocumentTarget(1, builder.ExpectedName)), ct);

        // Drain initial snapshot: ADD + CURRENT + NO_CHANGE (doc doesn't exist yet)
        await DrainAsync(call, 3, ct);

        // Create the document — triggers a notification
        var createTask = _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: ct);

        var docChange = await ReadNextAsync(call, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentChange, docChange.ResponseTypeCase);
        Assert.Equal(builder.ExpectedName, docChange.DocumentChange.Document.Name);
        Assert.Equal("hello", docChange.DocumentChange.Document.Fields["v"].StringValue);

        await createTask;
        await call.RequestStream.CompleteAsync();
    }

    [Fact]
    public async Task Listen_DocumentTarget_UpdateDocument_ReceivesDocumentChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder().WithCollection("listen-live").WithId("update-1").WithField("v", "original");
        await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: ct);

        using var call = _client.Listen(cancellationToken: ct);
        await call.RequestStream.WriteAsync(AddTargetRequest(BuildDocumentTarget(1, builder.ExpectedName)), ct);

        // Drain initial snapshot: ADD + DocumentChange + CURRENT + NO_CHANGE
        await DrainAsync(call, 4, ct);

        var updated = new DocumentBuilder().WithCollection("listen-live").WithId("update-1").WithField("v", "updated");
        var updateTask = _client.UpdateDocumentAsync(updated.BuildUpdateRequest(), cancellationToken: ct);

        var docChange = await ReadNextAsync(call, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentChange, docChange.ResponseTypeCase);
        Assert.Equal("updated", docChange.DocumentChange.Document.Fields["v"].StringValue);

        await updateTask;
        await call.RequestStream.CompleteAsync();
    }

    [Fact]
    public async Task Listen_DocumentTarget_DeleteDocument_ReceivesDocumentDelete()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder().WithCollection("listen-live").WithId("delete-1").WithField("v", "bye");
        await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: ct);

        using var call = _client.Listen(cancellationToken: ct);
        await call.RequestStream.WriteAsync(AddTargetRequest(BuildDocumentTarget(1, builder.ExpectedName)), ct);
        await DrainAsync(call, 4, ct);

        var deleteTask = _client.DeleteDocumentAsync(builder.BuildDeleteRequest(), cancellationToken: ct);

        var docDelete = await ReadNextAsync(call, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentDelete, docDelete.ResponseTypeCase);
        Assert.Equal(builder.ExpectedName, docDelete.DocumentDelete.Document);
        Assert.Contains(1, docDelete.DocumentDelete.RemovedTargetIds);

        await deleteTask;
        await call.RequestStream.CompleteAsync();
    }

    // ── Remove target ───────────────────────────────────────────────────────

    [Fact]
    public async Task Listen_RemoveTarget_ReceivesTargetChangeRemove()
    {
        var ct = TestContext.Current.CancellationToken;
        var resourceName = $"{Database}/documents/listen-remove/doc-1";

        using var call = _client.Listen(cancellationToken: ct);
        await call.RequestStream.WriteAsync(AddTargetRequest(BuildDocumentTarget(5, resourceName)), ct);
        await DrainAsync(call, 3, ct); // ADD + CURRENT + NO_CHANGE

        await call.RequestStream.WriteAsync(RemoveTargetRequest(5), ct);

        var remove = await ReadNextAsync(call, ct);
        Assert.Equal(TargetChange.Types.TargetChangeType.Remove, remove.TargetChange.TargetChangeType);
        Assert.Contains(5, remove.TargetChange.TargetIds);

        await call.RequestStream.CompleteAsync();
    }

    // ── Query target: initial snapshot ─────────────────────────────────────

    [Fact]
    public async Task Listen_QueryTarget_InitialSnapshot_OnlyMatchingDocumentsDelivered()
    {
        var ct = TestContext.Current.CancellationToken;
        var collectionId = "listen-query-init";

        var match = new DocumentBuilder().WithCollection(collectionId).WithId("q-match-1").WithField("status", "active");
        var noMatch = new DocumentBuilder().WithCollection(collectionId).WithId("q-nomatch-1").WithField("status", "inactive");
        await _client.CreateDocumentAsync(match.BuildCreateRequest(), cancellationToken: ct);
        await _client.CreateDocumentAsync(noMatch.BuildCreateRequest(), cancellationToken: ct);

        var target = BuildQueryTarget(1, $"{Database}/documents", collectionId,
            EqualFilter("status", "active"));

        using var call = _client.Listen(cancellationToken: ct);
        await call.RequestStream.WriteAsync(AddTargetRequest(target), ct);

        var add = await ReadNextAsync(call, ct);
        Assert.Equal(TargetChange.Types.TargetChangeType.Add, add.TargetChange.TargetChangeType);

        // Only the matching document
        var docChange = await ReadNextAsync(call, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentChange, docChange.ResponseTypeCase);
        Assert.Equal(match.ExpectedName, docChange.DocumentChange.Document.Name);

        var current = await ReadNextAsync(call, ct);
        Assert.Equal(TargetChange.Types.TargetChangeType.Current, current.TargetChange.TargetChangeType);

        await AssertNoMoreResponsesAsync(call, ct);

        await call.RequestStream.CompleteAsync();
    }

    // ── Query target: live notifications ───────────────────────────────────

    [Fact]
    public async Task Listen_QueryTarget_CreateMatchingDocument_ReceivesDocumentChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var collectionId = "listen-query-live";
        var target = BuildQueryTarget(1, $"{Database}/documents", collectionId,
            EqualFilter("region", "US"));

        using var call = _client.Listen(cancellationToken: ct);
        await call.RequestStream.WriteAsync(AddTargetRequest(target), ct);
        await DrainAsync(call, 3, ct); // ADD + CURRENT + NO_CHANGE (empty collection)

        var order = new DocumentBuilder().WithCollection(collectionId).WithId("order-us-1").WithField("region", "US");
        var createTask = _client.CreateDocumentAsync(order.BuildCreateRequest(), cancellationToken: ct);

        var docChange = await ReadNextAsync(call, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentChange, docChange.ResponseTypeCase);
        Assert.Equal(order.ExpectedName, docChange.DocumentChange.Document.Name);

        await createTask;
        await call.RequestStream.CompleteAsync();
    }

    [Fact]
    public async Task Listen_QueryTarget_UpdateDocumentToNoLongerMatch_ReceivesDocumentRemove()
    {
        var ct = TestContext.Current.CancellationToken;
        var collectionId = "listen-query-remove";
        var order = new DocumentBuilder().WithCollection(collectionId).WithId("order-eu-1").WithField("region", "US");
        await _client.CreateDocumentAsync(order.BuildCreateRequest(), cancellationToken: ct);

        var target = BuildQueryTarget(1, $"{Database}/documents", collectionId,
            EqualFilter("region", "US"));

        using var call = _client.Listen(cancellationToken: ct);
        await call.RequestStream.WriteAsync(AddTargetRequest(target), ct);
        await DrainAsync(call, 4, ct); // ADD + DocumentChange + CURRENT + NO_CHANGE

        var updated = new DocumentBuilder().WithCollection(collectionId).WithId("order-eu-1").WithField("region", "EU");
        var updateTask = _client.UpdateDocumentAsync(updated.BuildUpdateRequest(), cancellationToken: ct);

        var docRemove = await ReadNextAsync(call, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentRemove, docRemove.ResponseTypeCase);
        Assert.Equal(order.ExpectedName, docRemove.DocumentRemove.Document);

        await updateTask;
        await call.RequestStream.CompleteAsync();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static ListenRequest AddTargetRequest(Target target) =>
        new() { Database = Database, AddTarget = target };

    private static ListenRequest RemoveTargetRequest(int targetId) =>
        new() { Database = Database, RemoveTarget = targetId };

    private static Target BuildDocumentTarget(int targetId, params string[] documentNames)
    {
        var target = new Target
        {
            TargetId = targetId,
            Documents = new Target.Types.DocumentsTarget(),
        };
        target.Documents.Documents.AddRange(documentNames);
        return target;
    }

    private static Target BuildQueryTarget(int targetId, string parent, string collectionId,
        StructuredQuery.Types.Filter? filter = null)
    {
        var query = new StructuredQuery();
        query.From.Add(new StructuredQuery.Types.CollectionSelector { CollectionId = collectionId });
        if (filter is not null) query.Where = filter;

        return new Target
        {
            TargetId = targetId,
            Query = new Target.Types.QueryTarget { Parent = parent, StructuredQuery = query },
        };
    }

    private static StructuredQuery.Types.Filter EqualFilter(string field, string value) =>
        new()
        {
            FieldFilter = new StructuredQuery.Types.FieldFilter
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = field },
                Op = StructuredQuery.Types.FieldFilter.Types.Operator.Equal,
                Value = new Value { StringValue = value },
            },
        };

    private static async Task<ListenResponse> ReadNextAsync(
        AsyncDuplexStreamingCall<ListenRequest, ListenResponse> call,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        Assert.True(await call.ResponseStream.MoveNext(cts.Token));
        return call.ResponseStream.Current;
    }

    private static async Task DrainAsync(
        AsyncDuplexStreamingCall<ListenRequest, ListenResponse> call,
        int count,
        CancellationToken ct)
    {
        for (var i = 0; i < count; i++)
        {
            await ReadNextAsync(call, ct);
        }
    }

    private static async Task AssertNoMoreResponsesAsync(
        AsyncDuplexStreamingCall<ListenRequest, ListenResponse> call,
        CancellationToken ct)
    {
        while (true)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(200));
            try
            {
                var hasMore = await call.ResponseStream.MoveNext(cts.Token);
                if (!hasMore) return;
                // Skip NO_CHANGE heartbeats — they are protocol-level snapshot signals, not application data.
                if (call.ResponseStream.Current is
                    {
                        ResponseTypeCase: ListenResponse.ResponseTypeOneofCase.TargetChange,
                        TargetChange.TargetChangeType: TargetChange.Types.TargetChangeType.NoChange,
                    })
                {
                    continue;
                }
                Assert.Fail($"Expected no more responses but received: {call.ResponseStream.Current}");
            }
            catch (OperationCanceledException) 
            {
                // expected — no more responses
                return; 
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) 
            {
                // gRPC wraps cancellation
                return;
            }
        }
    }
}
