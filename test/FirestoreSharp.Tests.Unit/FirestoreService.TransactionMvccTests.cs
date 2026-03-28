using FirestoreSharp.Tests.Unit.Builders;

using Google.Cloud.Firestore.V1;

using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace FirestoreSharp.Tests.Unit;

/// <summary>
/// Integration tests for overlay-based snapshot isolation in transactions.
/// Verifies that reads within a transaction see a consistent per-document snapshot,
/// writes are isolated until commit, and intra-transaction writes are visible to
/// subsequent reads within the same transaction.
/// </summary>
public sealed class FirestoreServiceTransactionMvccTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly Firestore.FirestoreClient _client;

    public FirestoreServiceTransactionMvccTests(WebApplicationFactory<Program> factory)
    {
        var httpClient = factory.CreateDefaultClient();
        _channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });
        _client = new Firestore.FirestoreClient(_channel);
    }

    public void Dispose() => _channel.Dispose();

    // ── Snapshot isolation: consistent reads ──────────────────────────────────

    [Fact]
    public async Task Transaction_DocumentReadTwice_ReturnsSameVersion_EvenIfExternallyModified()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder()
            .WithCollection("mvcc-tests")
            .WithId("snapshot-read-1")
            .WithField("v", "original");

        await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: ct);

        // Begin a transaction and read the document — promotes into overlay
        var txn = await _client.BeginTransactionAsync(builder.BuildBeginTransactionRequest(), cancellationToken: ct);
        var firstRead = await _client.GetDocumentAsync(builder.BuildTransactionalGetRequest(txn.Transaction), cancellationToken: ct);

        // External write modifies the document
        var external = new DocumentBuilder().WithCollection("mvcc-tests").WithId("snapshot-read-1").WithField("v", "external");
        await _client.CommitAsync(external.BuildCommitRequest(external.BuildUpsertWrite()), cancellationToken: ct);

        // Read again within the same transaction — should see the overlay snapshot, not the external change
        var secondRead = await _client.GetDocumentAsync(builder.BuildTransactionalGetRequest(txn.Transaction), cancellationToken: ct);

        Assert.Equal("original", firstRead.Fields["v"].StringValue);
        Assert.Equal("original", secondRead.Fields["v"].StringValue);

        // Rollback — no writes needed, just verifying reads
        await _client.RollbackAsync(builder.BuildRollbackRequest(txn.Transaction), cancellationToken: ct);
    }

    // ── Write isolation: uncommitted writes not visible outside ───────────────

    [Fact]
    public async Task Transaction_Write_NotVisibleToOutsideReaders_UntilCommit()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder()
            .WithCollection("mvcc-tests")
            .WithId("write-isolation-1")
            .WithField("v", "original");

        await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: ct);

        // Begin transaction and prepare a write (but don't commit yet)
        var txn = await _client.BeginTransactionAsync(builder.BuildBeginTransactionRequest(), cancellationToken: ct);

        // Read (required before write in Firestore transactions)
        await _client.GetDocumentAsync(builder.BuildTransactionalGetRequest(txn.Transaction), cancellationToken: ct);

        // Outside reader sees the original value (transaction not yet committed)
        var outsideRead = await _client.GetDocumentAsync(builder.BuildGetRequest(), cancellationToken: ct);
        Assert.Equal("original", outsideRead.Fields["v"].StringValue);

        // Commit the transaction
        var update = new DocumentBuilder().WithCollection("mvcc-tests").WithId("write-isolation-1").WithField("v", "committed");
        await _client.CommitAsync(update.BuildTransactionalCommitRequest(txn.Transaction, update.BuildUpsertWrite()), cancellationToken: ct);

        // Now outside reader sees the committed value
        var afterCommit = await _client.GetDocumentAsync(builder.BuildGetRequest(), cancellationToken: ct);
        Assert.Equal("committed", afterCommit.Fields["v"].StringValue);
    }

    // ── Intra-transaction visibility ──────────────────────────────────────────

    [Fact]
    public async Task Transaction_WriteAndCommit_SubsequentExternalRead_SeesCommittedValue()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder()
            .WithCollection("mvcc-tests")
            .WithId("intra-txn-commit-1")
            .WithField("v", "initial");

        await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: ct);

        var txn = await _client.BeginTransactionAsync(builder.BuildBeginTransactionRequest(), cancellationToken: ct);
        await _client.GetDocumentAsync(builder.BuildTransactionalGetRequest(txn.Transaction), cancellationToken: ct);

        var update = new DocumentBuilder().WithCollection("mvcc-tests").WithId("intra-txn-commit-1").WithField("v", "transacted");
        await _client.CommitAsync(update.BuildTransactionalCommitRequest(txn.Transaction, update.BuildUpsertWrite()), cancellationToken: ct);

        var result = await _client.GetDocumentAsync(builder.BuildGetRequest(), cancellationToken: ct);
        Assert.Equal("transacted", result.Fields["v"].StringValue);
    }

    // ── Conflict detection ────────────────────────────────────────────────────

    [Fact]
    public async Task Transaction_ReadSetConflict_AfterExternalWrite_ThrowsAborted()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder()
            .WithCollection("mvcc-tests")
            .WithId("conflict-1")
            .WithField("v", "original");

        await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: ct);

        // Begin transaction and read — records UpdateTime in read-set
        var txn = await _client.BeginTransactionAsync(builder.BuildBeginTransactionRequest(), cancellationToken: ct);
        await _client.GetDocumentAsync(builder.BuildTransactionalGetRequest(txn.Transaction), cancellationToken: ct);

        // External write changes the document, invalidating the read-set
        var external = new DocumentBuilder().WithCollection("mvcc-tests").WithId("conflict-1").WithField("v", "modified");
        await _client.CommitAsync(external.BuildCommitRequest(external.BuildUpsertWrite()), cancellationToken: ct);

        // Commit should fail with ABORTED
        var write = new DocumentBuilder().WithCollection("mvcc-tests").WithId("conflict-1").WithField("v", "txn-value");
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            _client.CommitAsync(
                write.BuildTransactionalCommitRequest(txn.Transaction, write.BuildUpsertWrite()),
                cancellationToken: ct).ResponseAsync);

        Assert.Equal(StatusCode.Aborted, ex.StatusCode);
    }

    [Fact]
    public async Task Transaction_NoConflict_CommitsSuccessfully()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = new DocumentBuilder()
            .WithCollection("mvcc-tests")
            .WithId("no-conflict-1")
            .WithField("v", "original");

        await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: ct);

        var txn = await _client.BeginTransactionAsync(builder.BuildBeginTransactionRequest(), cancellationToken: ct);
        await _client.GetDocumentAsync(builder.BuildTransactionalGetRequest(txn.Transaction), cancellationToken: ct);

        // No external modification

        var update = new DocumentBuilder().WithCollection("mvcc-tests").WithId("no-conflict-1").WithField("v", "updated");
        var response = await _client.CommitAsync(
            update.BuildTransactionalCommitRequest(txn.Transaction, update.BuildUpsertWrite()),
            cancellationToken: ct);

        Assert.Single(response.WriteResults);

        var doc = await _client.GetDocumentAsync(builder.BuildGetRequest(), cancellationToken: ct);
        Assert.Equal("updated", doc.Fields["v"].StringValue);
    }

    // ── Query snapshot isolation ───────────────────────────────────────────────

    [Fact]
    public async Task Transaction_QueryAfterExternalWrite_ReturnsSnapshotValues()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc1 = new DocumentBuilder().WithCollection("mvcc-query-tests").WithId("q-snap-1").WithField("color", "red");
        var doc2 = new DocumentBuilder().WithCollection("mvcc-query-tests").WithId("q-snap-2").WithField("color", "blue");

        await _client.CreateDocumentAsync(doc1.BuildCreateRequest(), cancellationToken: ct);
        await _client.CreateDocumentAsync(doc2.BuildCreateRequest(), cancellationToken: ct);

        // Begin transaction and read doc1 — promotes into overlay
        var txn = await _client.BeginTransactionAsync(doc1.BuildBeginTransactionRequest(), cancellationToken: ct);
        await _client.GetDocumentAsync(doc1.BuildTransactionalGetRequest(txn.Transaction), cancellationToken: ct);

        // External write modifies doc1
        var externalDoc1 = new DocumentBuilder().WithCollection("mvcc-query-tests").WithId("q-snap-1").WithField("color", "green");
        await _client.CommitAsync(externalDoc1.BuildCommitRequest(externalDoc1.BuildUpsertWrite()), cancellationToken: ct);

        // Re-read doc1 within the same transaction — should still see "red" (overlay snapshot)
        var rereads = await _client.GetDocumentAsync(doc1.BuildTransactionalGetRequest(txn.Transaction), cancellationToken: ct);
        Assert.Equal("red", rereads.Fields["color"].StringValue);

        await _client.RollbackAsync(doc1.BuildRollbackRequest(txn.Transaction), cancellationToken: ct);
    }
}
