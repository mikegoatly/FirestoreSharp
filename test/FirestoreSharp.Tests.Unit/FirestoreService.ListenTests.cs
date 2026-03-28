using FirestoreSharp.Tests.Unit.Builders;
using Google.Cloud.Firestore.V1;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

using Value = Google.Cloud.Firestore.V1.Value;

namespace FirestoreSharp.Tests.Unit;

/// <summary>
/// Integration tests for the Listen RPC — these go through the real gRPC channel and
/// exercise the full stack: FirestoreGrpcService → ListenerService → ListenerConnection,
/// with mutations triggered via other gRPC calls.
/// </summary>
public sealed class FirestoreServiceListenTests(WebApplicationFactory<Program> factory)
    : FirestoreServiceTestBase(factory)
{
    private const string Database = "projects/test-project/databases/(default)";

    // ── Document target: initial snapshot ──────────────────────────────────

    [Fact]
    public async Task Listen_DocumentTarget_ExistingDocument_InitialSnapshotContainsDocument()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder().WithCollection("listen-init").WithId("existing-1").WithField("x", "a");
        await Client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: ct);

        using var call = Client.Listen(cancellationToken: ct);
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

        using var call = Client.Listen(cancellationToken: ct);
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

        using var call = Client.Listen(cancellationToken: ct);
        await call.RequestStream.WriteAsync(AddTargetRequest(BuildDocumentTarget(1, builder.ExpectedName)), ct);
        await DrainInitialSnapshotAsync(call, ct);

        // Create the document — triggers a notification
        var createTask = Client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: ct);

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
        await Client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: ct);

        using var call = Client.Listen(cancellationToken: ct);
        await call.RequestStream.WriteAsync(AddTargetRequest(BuildDocumentTarget(1, builder.ExpectedName)), ct);
        await DrainInitialSnapshotAsync(call, ct);

        var updated = new DocumentBuilder().WithCollection("listen-live").WithId("update-1").WithField("v", "updated");
        var updateTask = Client.UpdateDocumentAsync(updated.BuildUpdateRequest(), cancellationToken: ct);

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
        await Client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: ct);

        using var call = Client.Listen(cancellationToken: ct);
        await call.RequestStream.WriteAsync(AddTargetRequest(BuildDocumentTarget(1, builder.ExpectedName)), ct);
        await DrainInitialSnapshotAsync(call, ct);

        var deleteTask = Client.DeleteDocumentAsync(builder.BuildDeleteRequest(), cancellationToken: ct);

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

        using var call = Client.Listen(cancellationToken: ct);
        await call.RequestStream.WriteAsync(AddTargetRequest(BuildDocumentTarget(5, resourceName)), ct);
        await DrainInitialSnapshotAsync(call, ct);

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
        await Client.CreateDocumentAsync(match.BuildCreateRequest(), cancellationToken: ct);
        await Client.CreateDocumentAsync(noMatch.BuildCreateRequest(), cancellationToken: ct);

        var target = BuildQueryTarget(1, $"{Database}/documents", collectionId,
            EqualFilter("status", "active"));

        using var call = Client.Listen(cancellationToken: ct);
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

        using var call = Client.Listen(cancellationToken: ct);
        await call.RequestStream.WriteAsync(AddTargetRequest(target), ct);
        await DrainInitialSnapshotAsync(call, ct);

        var order = new DocumentBuilder().WithCollection(collectionId).WithId("order-us-1").WithField("region", "US");
        var createTask = Client.CreateDocumentAsync(order.BuildCreateRequest(), cancellationToken: ct);

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
        await Client.CreateDocumentAsync(order.BuildCreateRequest(), cancellationToken: ct);

        var target = BuildQueryTarget(1, $"{Database}/documents", collectionId,
            EqualFilter("region", "US"));

        using var call = Client.Listen(cancellationToken: ct);
        await call.RequestStream.WriteAsync(AddTargetRequest(target), ct);
        await DrainInitialSnapshotAsync(call, ct);

        var updated = new DocumentBuilder().WithCollection(collectionId).WithId("order-eu-1").WithField("region", "EU");
        var updateTask = Client.UpdateDocumentAsync(updated.BuildUpdateRequest(), cancellationToken: ct);

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
        if (filter is not null)
        {
            query.Where = filter;
        }

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
}
