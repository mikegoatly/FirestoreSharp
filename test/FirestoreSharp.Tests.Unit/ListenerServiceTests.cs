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
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Add, targetId: 1);
        await AssertDocumentChangeAsync(connection, ct, builder.ExpectedName, targetId: 1);
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Current);
    }

    [Fact]
    public async Task DocumentTarget_InitialSnapshot_MissingDocument_SendsOnlyTargetChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var resourceName = "projects/test-project/databases/(default)/documents/users/nonexistent";

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(BuildDocumentTarget(1, resourceName), ct);

        // Expect: TargetChange(ADD), TargetChange(CURRENT) — no DocumentChange
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Add);
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Current);
    }

    // ── Document target: live mutations ────────────────────────────────────

    [Fact]
    public async Task DocumentTarget_CreateWatchedDocument_ReceivesDocumentChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder().WithCollection("users").WithId("u2").WithField("name", "Bob");

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(BuildDocumentTarget(1, builder.ExpectedName), ct);

        await DrainInitialSnapshotAsync(connection, ct);

        // Now create the document
        await _documentService.CreateAsync(builder.BuildPath(), builder.Build(), ct);

        var docChange = await AssertDocumentChangeAsync(connection, ct, builder.ExpectedName);
        Assert.Equal("Bob", docChange.Document.Fields["name"].StringValue);
    }

    [Fact]
    public async Task DocumentTarget_UpdateWatchedDocument_ReceivesDocumentChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder().WithCollection("users").WithId("u3").WithField("name", "Carol");
        await _documentService.CreateAsync(builder.BuildPath(), builder.Build(), ct);

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(BuildDocumentTarget(1, builder.ExpectedName), ct);
        await DrainInitialSnapshotAsync(connection, ct);

        // Update the document
        var updatedBuilder = new DocumentBuilder().WithCollection("users").WithId("u3").WithField("name", "Carol Updated");
        await _documentService.UpdateAsync(updatedBuilder.BuildPath(), updatedBuilder.Build(), null, ct);

        var docChange = await AssertDocumentChangeAsync(connection, ct);
        Assert.Equal("Carol Updated", docChange.Document.Fields["name"].StringValue);
    }

    [Fact]
    public async Task DocumentTarget_DeleteWatchedDocument_ReceivesDocumentDelete()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder().WithCollection("users").WithId("u4").WithField("name", "Dan");
        await _documentService.CreateAsync(builder.BuildPath(), builder.Build(), ct);

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(BuildDocumentTarget(1, builder.ExpectedName), ct);
        await DrainInitialSnapshotAsync(connection, ct);

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
        await DrainInitialSnapshotAsync(connection, ct);

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
        await DrainInitialSnapshotAsync(connection, ct);

        connection.RemoveTarget(7);

        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Remove, targetId: 7);
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
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Add);
        await AssertDocumentChangeAsync(connection, ct, item1.ExpectedName);
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Current);
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
        await DrainInitialSnapshotAsync(connection, ct);

        // Create a matching document
        var order = new DocumentBuilder().WithCollection("orders").WithId("o1").WithField("region", "US");
        await _documentService.CreateAsync(order.BuildPath(), order.Build(), ct);

        await AssertDocumentChangeAsync(connection, ct, order.ExpectedName);
    }

    [Fact]
    public async Task QueryTarget_CreateNonMatchingDocument_ReceivesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = BuildQueryTarget(1, "projects/test-project/databases/(default)/documents",
            "orders", filter: EqualFilter("region", "US"));

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(query, ct);
        await DrainInitialSnapshotAsync(connection, ct);

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
        await DrainInitialSnapshotAsync(connection, ct);

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
        await DrainInitialSnapshotAsync(connection, ct);

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
        await DrainInitialSnapshotAsync(conn1, ct);
        await DrainInitialSnapshotAsync(conn2, ct);

        await _documentService.CreateAsync(builder.BuildPath(), builder.Build(), ct);

        await AssertDocumentChangeAsync(conn1, ct, builder.ExpectedName);
        await AssertDocumentChangeAsync(conn2, ct, builder.ExpectedName);
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
        await DrainInitialSnapshotAsync(connection, ct);
        await DrainInitialSnapshotAsync(connection, ct);

        // Commit both writes atomically
        var writes = new[]
        {
            new Write { Update = d1.Build() },
            new Write { Update = d2.Build() },
        };

        await _documentService.CommitAsync(writes, null, null, ct);

        var c1 = await AssertDocumentChangeAsync(connection, ct);
        var c2 = await AssertDocumentChangeAsync(connection, ct);

        var names = new[] { c1.Document.Name, c2.Document.Name };
        Assert.Contains(d1.ExpectedName, names);
        Assert.Contains(d2.ExpectedName, names);
    }

    // ── once flag ──────────────────────────────────────────────────────────

    [Fact]
    public async Task OnceTarget_DocumentTarget_RemovesAfterInitialSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder().WithCollection("users").WithId("once1").WithField("name", "Eve");
        await _documentService.CreateAsync(builder.BuildPath(), builder.Build(), ct);

        await using var connection = _listenerService.CreateConnection();
        var target = BuildDocumentTarget(1, builder.ExpectedName);
        target.Once = true;
        await connection.AddTargetAsync(target, ct);

        // ADD, DocumentChange, CURRENT, ExistenceFilter, NO_CHANGE, then REMOVE
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Add, targetId: 1);
        await AssertDocumentChangeAsync(connection, ct);
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Current);
        await ReadResponseAsync(connection, ct); // ExistenceFilter
        await ReadResponseAsync(connection, ct); // NO_CHANGE
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Remove, targetId: 1);
    }

    [Fact]
    public async Task OnceTarget_NoFurtherNotificationsAfterRemove()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder().WithCollection("users").WithId("once2").WithField("name", "Frank");

        await using var connection = _listenerService.CreateConnection();
        var target = BuildDocumentTarget(1, builder.ExpectedName);
        target.Once = true;
        await connection.AddTargetAsync(target, ct);

        // No document exists, so the sequence is: ADD, CURRENT, ExistenceFilter, NO_CHANGE, REMOVE
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Add);
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Current);
        await ReadResponseAsync(connection, ct); // ExistenceFilter
        await ReadResponseAsync(connection, ct); // NO_CHANGE
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Remove);

        // Now create the document — should NOT produce any notification since target was removed
        await _documentService.CreateAsync(builder.BuildPath(), builder.Build(), ct);

        Assert.False(connection.Responses.TryRead(out _));
    }

    [Fact]
    public async Task OnceTarget_QueryTarget_RemovesAfterInitialSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var item = new DocumentBuilder().WithCollection("products").WithId("p1").WithField("inStock", true);
        await _documentService.CreateAsync(item.BuildPath(), item.Build(), ct);

        await using var connection = _listenerService.CreateConnection();
        var target = BuildQueryTarget(2, "projects/test-project/databases/(default)/documents", "products");
        target.Once = true;
        await connection.AddTargetAsync(target, ct);

        // ADD, DocumentChange, CURRENT, ExistenceFilter, NO_CHANGE, REMOVE
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Add);
        await AssertDocumentChangeAsync(connection, ct);
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Current);
        await ReadResponseAsync(connection, ct); // ExistenceFilter
        await ReadResponseAsync(connection, ct); // NO_CHANGE
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Remove, targetId: 2);

        // Further mutations should not reach this target
        var item2 = new DocumentBuilder().WithCollection("products").WithId("p2").WithField("inStock", false);
        await _documentService.CreateAsync(item2.BuildPath(), item2.Build(), ct);

        Assert.False(connection.Responses.TryRead(out _));
    }

    // ── ExistenceFilter ────────────────────────────────────────────────────

    [Fact]
    public async Task ExistenceFilter_EmptyTarget_HasZeroCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var resourceName = "projects/test-project/databases/(default)/documents/ef/missing";

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(BuildDocumentTarget(1, resourceName), ct);

        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Add);
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Current);

        var filterResponse = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.Filter, filterResponse.ResponseTypeCase);
        Assert.Equal(1, filterResponse.Filter.TargetId);
        Assert.Equal(0, filterResponse.Filter.Count);
        Assert.Null(filterResponse.Filter.UnchangedNames); // no bloom filter needed for empty set
    }

    [Fact]
    public async Task ExistenceFilter_WithDocuments_HasCorrectCountAndBloomFilter()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc1 = new DocumentBuilder().WithCollection("ef-col").WithId("d1").WithField("x", "1");
        var doc2 = new DocumentBuilder().WithCollection("ef-col").WithId("d2").WithField("x", "2");
        await _documentService.CreateAsync(doc1.BuildPath(), doc1.Build(), ct);
        await _documentService.CreateAsync(doc2.BuildPath(), doc2.Build(), ct);

        var query = BuildQueryTarget(1, "projects/test-project/databases/(default)/documents", "ef-col");
        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(query, ct);

        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Add);
        await AssertDocumentChangeAsync(connection, ct);
        await AssertDocumentChangeAsync(connection, ct);
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Current);

        var filterResponse = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.Filter, filterResponse.ResponseTypeCase);
        var filter = filterResponse.Filter;
        Assert.Equal(1, filter.TargetId);
        Assert.Equal(2, filter.Count);
        Assert.NotNull(filter.UnchangedNames);
        Assert.True(filter.UnchangedNames.HashCount >= 1);
        Assert.NotEmpty(filter.UnchangedNames.Bits.Bitmap);
    }

    [Fact]
    public async Task ExistenceFilter_BloomFilter_ContainsAllActiveDocuments()
    {
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var docs = Enumerable.Range(1, 5)
            .Select(i => new DocumentBuilder().WithCollection($"ef-bloom-{suffix}").WithId($"d{i}"))
            .ToList();

        foreach (var doc in docs)
        {
            await _documentService.CreateAsync(doc.BuildPath(), doc.Build(), ct);
        }

        var query = BuildQueryTarget(1, "projects/test-project/databases/(default)/documents", $"ef-bloom-{suffix}");
        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(query, ct);

        // Drain ADD + 5 DocumentChanges + CURRENT
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Add);
        for (var i = 0; i < 5; i++) await AssertDocumentChangeAsync(connection, ct);
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Current);

        var filterResponse = await ReadResponseAsync(connection, ct);
        var bloomFilter = filterResponse.Filter.UnchangedNames;
        Assert.NotNull(bloomFilter);

        // Every active document must test positive in the bloom filter (no false negatives)
        foreach (var doc in docs)
        {
            Assert.True(
                BloomFilterBuilder.MightContain(bloomFilter, doc.ExpectedName),
                $"Bloom filter must contain '{doc.ExpectedName}'");
        }
    }

    // ── Target ID auto-assignment ──────────────────────────────────────────

    [Fact]
    public async Task TargetIdZero_ServerAssignsId()
    {
        var ct = TestContext.Current.CancellationToken;
        var resourceName = "projects/test-project/databases/(default)/documents/auto/a1";

        await using var connection = _listenerService.CreateConnection();
        await connection.AddTargetAsync(BuildDocumentTarget(0, resourceName), ct);

        var add = await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Add);
        // Server assigned an ID — it should be > 0
        Assert.True(add.TargetIds[0] > 0);
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

    private static async Task<TargetChange> AssertTargetChangeAsync(
        IListenerConnection connection,
        CancellationToken ct,
        TargetChange.Types.TargetChangeType expectedType,
        int? targetId = null)
    {
        var response = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.TargetChange, response.ResponseTypeCase);
        Assert.Equal(expectedType, response.TargetChange.TargetChangeType);
        if (targetId.HasValue)
        {
            Assert.Contains(targetId.Value, response.TargetChange.TargetIds);
        }
        return response.TargetChange;
    }

    private static async Task<DocumentChange> AssertDocumentChangeAsync(
        IListenerConnection connection,
        CancellationToken ct,
        string? expectedDocName = null,
        int? targetId = null)
    {
        var response = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.DocumentChange, response.ResponseTypeCase);
        if (expectedDocName is not null)
        {
            Assert.Equal(expectedDocName, response.DocumentChange.Document.Name);
        }
        if (targetId.HasValue)
        {
            Assert.Contains(targetId.Value, response.DocumentChange.TargetIds);
        }
        return response.DocumentChange;
    }

    /// <summary>
    /// Drains the full initial-snapshot sequence for one target:
    /// ADD → 0..N DocumentChanges → CURRENT → ExistenceFilter → NO_CHANGE.
    /// Returns the documents received in the snapshot.
    /// Call once per AddTargetAsync invocation.
    /// </summary>
    private static async Task<IReadOnlyList<Document>> DrainInitialSnapshotAsync(
        IListenerConnection connection, CancellationToken ct)
    {
        await AssertTargetChangeAsync(connection, ct, TargetChange.Types.TargetChangeType.Add);

        var documents = new List<Document>();
        while (true)
        {
            var response = await ReadResponseAsync(connection, ct);
            if (response.ResponseTypeCase == ListenResponse.ResponseTypeOneofCase.DocumentChange)
            {
                documents.Add(response.DocumentChange.Document);
            }
            else
            {
                Assert.Equal(ListenResponse.ResponseTypeOneofCase.TargetChange, response.ResponseTypeCase);
                Assert.Equal(TargetChange.Types.TargetChangeType.Current, response.TargetChange.TargetChangeType);
                break;
            }
        }

        var filterResponse = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.Filter, filterResponse.ResponseTypeCase);

        var noChange = await ReadResponseAsync(connection, ct);
        Assert.Equal(ListenResponse.ResponseTypeOneofCase.TargetChange, noChange.ResponseTypeCase);
        Assert.Equal(TargetChange.Types.TargetChangeType.NoChange, noChange.TargetChange.TargetChangeType);

        return documents;
    }
}
