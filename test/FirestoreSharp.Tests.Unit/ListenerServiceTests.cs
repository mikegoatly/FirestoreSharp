using FirestoreSharp.Core;
using FirestoreSharp.Core.Listeners;
using FirestoreSharp.Core.Stores.InMemory;
using FirestoreSharp.Tests.Unit.Builders;

using Google.Cloud.Firestore.V1;

using Xunit;

using Value = Google.Cloud.Firestore.V1.Value;

namespace FirestoreSharp.Tests.Unit;

public sealed class ListenerServiceTests : IAsyncDisposable
{
    private readonly InMemoryDocumentStore _store = new();
    private readonly ListenerService _listenerService;
    private readonly DocumentService _documentService;

    public ListenerServiceTests()
    {
        _listenerService = new ListenerService(_store);
        _documentService = new DocumentService(_store, _listenerService);
    }

    public async ValueTask DisposeAsync()
    {
        _documentService.Dispose();
        await ValueTask.CompletedTask;
    }

    // ── Document target: initial snapshot ──────────────────────────────────

    [Fact]
    public async Task DocumentTarget_InitialSnapshot_SendsExistingDocument()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder().WithCollection("users").WithId("u1").WithField("name", "Alice");
        await _documentService.CreateAsync(builder.BuildPath(), builder.Build(), ct);

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(BuildDocumentTarget(1, builder.ExpectedName), ct);

        // Expect: TargetChange(ADD), DocumentChange, TargetChange(CURRENT)
        var add = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.TargetChange, add.ResponseTypeCase);
        Assert.Equal(TargetChange.Types.TargetChangeType.Add, add.TargetChange.TargetChangeType);
        Assert.Contains(1, add.TargetChange.TargetIds);

        var docChange = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentChange, docChange.ResponseTypeCase);
        Assert.Equal(builder.ExpectedName, docChange.DocumentChange.Document.Name);
        Assert.Contains(1, docChange.DocumentChange.TargetIds);

        var current = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.TargetChange, current.ResponseTypeCase);
        Assert.Equal(TargetChange.Types.TargetChangeType.Current, current.TargetChange.TargetChangeType);
    }

    [Fact]
    public async Task DocumentTarget_InitialSnapshot_MissingDocument_SendsOnlyTargetChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var resourceName = "projects/test-project/databases/(default)/documents/users/nonexistent";

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(BuildDocumentTarget(1, resourceName), ct);

        // Expect: TargetChange(ADD), TargetChange(CURRENT) — no DocumentChange
        var add = await ReadResponseAsync(connection, ct);
        Assert.Equal(TargetChange.Types.TargetChangeType.Add, add.TargetChange.TargetChangeType);

        var current = await ReadResponseAsync(connection, ct);
        Assert.Equal(TargetChange.Types.TargetChangeType.Current, current.TargetChange.TargetChangeType);
    }

    // ── Document target: live mutations ────────────────────────────────────

    [Fact]
    public async Task DocumentTarget_CreateWatchedDocument_ReceivesDocumentChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder().WithCollection("users").WithId("u2").WithField("name", "Bob");

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(BuildDocumentTarget(1, builder.ExpectedName), ct);

        // Drain initial snapshot (ADD + CURRENT + NO_CHANGE, no doc)
        await DrainResponsesAsync(connection, 3, ct);

        // Now create the document
        await _documentService.CreateAsync(builder.BuildPath(), builder.Build(), ct);

        var docChange = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentChange, docChange.ResponseTypeCase);
        Assert.Equal(builder.ExpectedName, docChange.DocumentChange.Document.Name);
        Assert.Equal("Bob", docChange.DocumentChange.Document.Fields["name"].StringValue);
    }

    [Fact]
    public async Task DocumentTarget_UpdateWatchedDocument_ReceivesDocumentChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder().WithCollection("users").WithId("u3").WithField("name", "Carol");
        await _documentService.CreateAsync(builder.BuildPath(), builder.Build(), ct);

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(BuildDocumentTarget(1, builder.ExpectedName), ct);
        await DrainResponsesAsync(connection, 4, ct); // ADD + DocumentChange + CURRENT + NO_CHANGE

        // Update the document
        var updatedBuilder = new DocumentBuilder().WithCollection("users").WithId("u3").WithField("name", "Carol Updated");
        await _documentService.UpdateAsync(updatedBuilder.BuildPath(), updatedBuilder.Build(), null, ct);

        var docChange = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentChange, docChange.ResponseTypeCase);
        Assert.Equal("Carol Updated", docChange.DocumentChange.Document.Fields["name"].StringValue);
    }

    [Fact]
    public async Task DocumentTarget_DeleteWatchedDocument_ReceivesDocumentDelete()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder().WithCollection("users").WithId("u4").WithField("name", "Dan");
        await _documentService.CreateAsync(builder.BuildPath(), builder.Build(), ct);

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(BuildDocumentTarget(1, builder.ExpectedName), ct);
        await DrainResponsesAsync(connection, 4, ct);

        await _documentService.DeleteAsync(builder.BuildPath(), ct);

        var docDelete = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentDelete, docDelete.ResponseTypeCase);
        Assert.Equal(builder.ExpectedName, docDelete.DocumentDelete.Document);
        Assert.Contains(1, docDelete.DocumentDelete.RemovedTargetIds);
    }

    [Fact]
    public async Task DocumentTarget_MutateUnwatchedDocument_ReceivesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var watched = new DocumentBuilder().WithCollection("users").WithId("watched");
        var unwatched = new DocumentBuilder().WithCollection("users").WithId("unwatched").WithField("x", "y");

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(BuildDocumentTarget(1, watched.ExpectedName), ct);
        await DrainResponsesAsync(connection, 3, ct);

        // Create an unwatched document — should not produce any notification
        await _documentService.CreateAsync(unwatched.BuildPath(), unwatched.Build(), ct);

        Assert.False(connection.Responses.TryRead(out _));
    }

    // ── Document target: remove target ─────────────────────────────────────

    [Fact]
    public async Task RemoveTarget_SendsTargetChangeRemove()
    {
        var ct = TestContext.Current.CancellationToken;
        var resourceName = "projects/test-project/databases/(default)/documents/users/rem1";

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(BuildDocumentTarget(7, resourceName), ct);
        await DrainResponsesAsync(connection, 3, ct);

        connection.RemoveTarget(7);

        var remove = await ReadResponseAsync(connection, ct);
        Assert.Equal(TargetChange.Types.TargetChangeType.Remove, remove.TargetChange.TargetChangeType);
        Assert.Contains(7, remove.TargetChange.TargetIds);
    }

    // ── Query target: initial snapshot ─────────────────────────────────────

    [Fact]
    public async Task QueryTarget_InitialSnapshot_SendsMatchingDocuments()
    {
        var ct = TestContext.Current.CancellationToken;

        // Create two documents in "items" collection
        var item1 = new DocumentBuilder().WithCollection("items").WithId("i1").WithField("status", "active");
        var item2 = new DocumentBuilder().WithCollection("items").WithId("i2").WithField("status", "inactive");
        await _documentService.CreateAsync(item1.BuildPath(), item1.Build(), ct);
        await _documentService.CreateAsync(item2.BuildPath(), item2.Build(), ct);

        // Query: items where status == "active"
        var query = BuildQueryTarget(1, "projects/test-project/databases/(default)/documents",
            "items", filter: EqualFilter("status", "active"));

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(query, ct);

        // Expect: ADD, DocumentChange(i1 only), CURRENT
        var add = await ReadResponseAsync(connection, ct);
        Assert.Equal(TargetChange.Types.TargetChangeType.Add, add.TargetChange.TargetChangeType);

        var docChange = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentChange, docChange.ResponseTypeCase);
        Assert.Equal(item1.ExpectedName, docChange.DocumentChange.Document.Name);

        var current = await ReadResponseAsync(connection, ct);
        Assert.Equal(TargetChange.Types.TargetChangeType.Current, current.TargetChange.TargetChangeType);
    }

    // ── Query target: live mutations ───────────────────────────────────────

    [Fact]
    public async Task QueryTarget_CreateMatchingDocument_ReceivesDocumentChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = BuildQueryTarget(1, "projects/test-project/databases/(default)/documents",
            "orders", filter: EqualFilter("region", "US"));

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(query, ct);
        await DrainResponsesAsync(connection, 3, ct); // ADD + CURRENT + NO_CHANGE

        // Create a matching document
        var order = new DocumentBuilder().WithCollection("orders").WithId("o1").WithField("region", "US");
        await _documentService.CreateAsync(order.BuildPath(), order.Build(), ct);

        var docChange = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentChange, docChange.ResponseTypeCase);
        Assert.Equal(order.ExpectedName, docChange.DocumentChange.Document.Name);
    }

    [Fact]
    public async Task QueryTarget_CreateNonMatchingDocument_ReceivesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = BuildQueryTarget(1, "projects/test-project/databases/(default)/documents",
            "orders", filter: EqualFilter("region", "US"));

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(query, ct);
        await DrainResponsesAsync(connection, 3, ct);

        // Create a non-matching document
        var order = new DocumentBuilder().WithCollection("orders").WithId("o2").WithField("region", "EU");
        await _documentService.CreateAsync(order.BuildPath(), order.Build(), ct);

        Assert.False(connection.Responses.TryRead(out _));
    }

    [Fact]
    public async Task QueryTarget_UpdateDocumentToNoLongerMatch_ReceivesDocumentRemove()
    {
        var ct = TestContext.Current.CancellationToken;

        // Create a matching document first
        var order = new DocumentBuilder().WithCollection("orders").WithId("o3").WithField("region", "US");
        await _documentService.CreateAsync(order.BuildPath(), order.Build(), ct);

        var query = BuildQueryTarget(1, "projects/test-project/databases/(default)/documents",
            "orders", filter: EqualFilter("region", "US"));

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(query, ct);
        await DrainResponsesAsync(connection, 4, ct); // ADD + DocChange + CURRENT + NO_CHANGE

        // Update the document so it no longer matches
        var updated = new DocumentBuilder().WithCollection("orders").WithId("o3").WithField("region", "EU");
        await _documentService.UpdateAsync(updated.BuildPath(), updated.Build(), null, ct);

        var docRemove = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentRemove, docRemove.ResponseTypeCase);
        Assert.Equal(order.ExpectedName, docRemove.DocumentRemove.Document);
        Assert.Contains(1, docRemove.DocumentRemove.RemovedTargetIds);
    }

    [Fact]
    public async Task QueryTarget_DeleteMatchingDocument_ReceivesDocumentDelete()
    {
        var ct = TestContext.Current.CancellationToken;

        var order = new DocumentBuilder().WithCollection("orders").WithId("o4").WithField("region", "US");
        await _documentService.CreateAsync(order.BuildPath(), order.Build(), ct);

        var query = BuildQueryTarget(1, "projects/test-project/databases/(default)/documents",
            "orders", filter: EqualFilter("region", "US"));

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(query, ct);
        await DrainResponsesAsync(connection, 4, ct);

        await _documentService.DeleteAsync(order.BuildPath(), ct);

        var docDelete = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentDelete, docDelete.ResponseTypeCase);
        Assert.Equal(order.ExpectedName, docDelete.DocumentDelete.Document);
    }

    // ── Multiple connections ───────────────────────────────────────────────

    [Fact]
    public async Task MultipleConnections_BothReceiveNotifications()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder().WithCollection("multi").WithId("m1").WithField("x", "y");

        await using var conn1 = _listenerService.CreateConnection();
        await using var conn2 = _listenerService.CreateConnection();

        await conn1.AddTargetAsync(BuildDocumentTarget(1, builder.ExpectedName), ct);
        await conn2.AddTargetAsync(BuildDocumentTarget(2, builder.ExpectedName), ct);
        await DrainResponsesAsync(conn1, 3, ct);
        await DrainResponsesAsync(conn2, 3, ct);

        await _documentService.CreateAsync(builder.BuildPath(), builder.Build(), ct);

        var change1 = await ReadResponseAsync(conn1, ct);
        var change2 = await ReadResponseAsync(conn2, ct);

        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentChange, change1.ResponseTypeCase);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentChange, change2.ResponseTypeCase);
        Assert.Equal(builder.ExpectedName, change1.DocumentChange.Document.Name);
        Assert.Equal(builder.ExpectedName, change2.DocumentChange.Document.Name);
    }

    // ── Connection disposal ────────────────────────────────────────────────

    [Fact]
    public async Task ConnectionDisposal_CompletesResponseChannel()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = _listenerService.CreateConnection();

        await connection.DisposeAsync();

        // After disposal, the channel reader should be completed
        Assert.True(connection.Responses.Completion.IsCompleted);
    }

    // ── CommitAsync batched notifications ──────────────────────────────────

    [Fact]
    public async Task CommitAsync_BatchNotification_ReceivesAllChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var d1 = new DocumentBuilder().WithCollection("batch").WithId("b1").WithField("v", "1");
        var d2 = new DocumentBuilder().WithCollection("batch").WithId("b2").WithField("v", "2");

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(BuildDocumentTarget(1, d1.ExpectedName), ct);
        await connection.AddTargetAsync(BuildDocumentTarget(2, d2.ExpectedName), ct);
        // Drain initial snapshots: (ADD + CURRENT + NO_CHANGE) * 2
        await DrainResponsesAsync(connection, 6, ct);

        // Commit both writes atomically
        var writes = new[]
        {
            new Write { Update = d1.Build() },
            new Write { Update = d2.Build() },
        };

        await _documentService.CommitAsync(writes, null, ct);

        var c1 = await ReadResponseAsync(connection, ct);
        var c2 = await ReadResponseAsync(connection, ct);

        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentChange, c1.ResponseTypeCase);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentChange, c2.ResponseTypeCase);

        var names = new[] { c1.DocumentChange.Document.Name, c2.DocumentChange.Document.Name };
        Assert.Contains(d1.ExpectedName, names);
        Assert.Contains(d2.ExpectedName, names);
    }

    // ── Target ID auto-assignment ──────────────────────────────────────────

    [Fact]
    public async Task TargetIdZero_ServerAssignsId()
    {
        var ct = TestContext.Current.CancellationToken;
        var resourceName = "projects/test-project/databases/(default)/documents/auto/a1";

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(BuildDocumentTarget(0, resourceName), ct);

        var add = await ReadResponseAsync(connection, ct);
        Assert.Equal(TargetChange.Types.TargetChangeType.Add, add.TargetChange.TargetChangeType);
        // Server assigned an ID — it should be > 0
        Assert.True(add.TargetChange.TargetIds[0] > 0);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

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

    private static Target BuildQueryTarget(int targetId, string parent, string collectionId, StructuredQuery.Types.Filter? filter = null)
    {
        var query = new StructuredQuery();
        query.From.Add(new StructuredQuery.Types.CollectionSelector { CollectionId = collectionId });

        if (filter is not null)
        {
            query.Where = filter;
        }

        return new Target
        {
            TargetId = targetId,
            Query = new Target.Types.QueryTarget
            {
                Parent = parent,
                StructuredQuery = query,
            },
        };
    }

    private static StructuredQuery.Types.Filter EqualFilter(string field, string value)
    {
        return new StructuredQuery.Types.Filter
        {
            FieldFilter = new StructuredQuery.Types.FieldFilter
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = field },
                Op = StructuredQuery.Types.FieldFilter.Types.Operator.Equal,
                Value = new Value { StringValue = value },
            },
        };
    }

    private static async Task<ListenResponse> ReadResponseAsync(IListenerConnection connection, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        return await connection.Responses.ReadAsync(cts.Token);
    }

    private static async Task DrainResponsesAsync(IListenerConnection connection, int count, CancellationToken ct)
    {
        for (var i = 0; i < count; i++)
        {
            await ReadResponseAsync(connection, ct);
        }
    }
}
