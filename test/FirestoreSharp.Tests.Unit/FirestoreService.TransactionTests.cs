using FirestoreSharp.Tests.Unit.Builders;
using Google.Cloud.Firestore.V1;
using Google.Protobuf;
using Grpc.Core;
using Xunit;

using Microsoft.AspNetCore.Mvc.Testing;

namespace FirestoreSharp.Tests.Unit;

public sealed class FirestoreServiceTransactionTests(WebApplicationFactory<Program> factory) : FirestoreServiceTestBase(factory)
{

    // ── Commit ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Commit_UpsertWrite_CreatesDocument()
    {
        var builder = new DocumentBuilder().WithCollection("commit-tests").WithId("commit-create-1").WithField("x", "hello");

        var response = await Client.CommitAsync(builder.BuildCommitRequest(builder.BuildUpsertWrite()), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(response.WriteResults);
        Assert.NotNull(response.WriteResults[0].UpdateTime);
        Assert.NotNull(response.CommitTime);

        var doc = await Client.GetDocumentAsync(builder.BuildGetRequest(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("hello", doc.Fields["x"].StringValue);
    }

    [Fact]
    public async Task Commit_UpsertWrite_OverwritesExistingDocument()
    {
        var builder = new DocumentBuilder().WithCollection("commit-tests").WithId("commit-overwrite-1").WithField("a", "original").WithField("b", "keep");
        await Client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var overwrite = new DocumentBuilder().WithCollection("commit-tests").WithId("commit-overwrite-1").WithField("a", "updated");
        await Client.CommitAsync(overwrite.BuildCommitRequest(overwrite.BuildUpsertWrite()), cancellationToken: TestContext.Current.CancellationToken);

        var doc = await Client.GetDocumentAsync(builder.BuildGetRequest(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("updated", doc.Fields["a"].StringValue);
        Assert.False(doc.Fields.ContainsKey("b"), "upsert without mask should replace all fields");
    }

    [Fact]
    public async Task Commit_MaskedUpdateWrite_MergesIntoExistingDocument()
    {
        var builder = new DocumentBuilder().WithCollection("commit-tests").WithId("commit-merge-1").WithField("a", "original").WithField("b", "keep");
        await Client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var update = new DocumentBuilder().WithCollection("commit-tests").WithId("commit-merge-1").WithField("a", "updated");
        await Client.CommitAsync(update.BuildCommitRequest(update.BuildMaskedUpdateWrite("a")), cancellationToken: TestContext.Current.CancellationToken);

        var doc = await Client.GetDocumentAsync(builder.BuildGetRequest(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("updated", doc.Fields["a"].StringValue);
        Assert.Equal("keep", doc.Fields["b"].StringValue);
    }

    [Fact]
    public async Task Commit_DeleteWrite_RemovesDocument()
    {
        var builder = new DocumentBuilder().WithCollection("commit-tests").WithId("commit-delete-1").WithField("x", "y");
        await Client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        await Client.CommitAsync(builder.BuildCommitRequest(builder.BuildDeleteWrite()), cancellationToken: TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            Client.GetDocumentAsync(builder.BuildGetRequest(), cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Commit_PreconditionExistsTrue_DocumentMissing_ThrowsFailedPrecondition()
    {
        var builder = new DocumentBuilder().WithCollection("commit-tests").WithId("commit-precond-1");
        var write = new Write { Update = builder.Build(), CurrentDocument = new Precondition { Exists = true } };

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            Client.CommitAsync(builder.BuildCommitRequest(write), cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);
        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }

    [Fact]
    public async Task Commit_PreconditionExistsFalse_DocumentExists_ThrowsFailedPrecondition()
    {
        var builder = new DocumentBuilder().WithCollection("commit-tests").WithId("commit-precond-2").WithField("x", "y");
        await Client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var write = new Write { Update = builder.Build(), CurrentDocument = new Precondition { Exists = false } };

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            Client.CommitAsync(builder.BuildCommitRequest(write), cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);
        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }

    [Fact]
    public async Task Commit_MultipleWrites_AllApplied()
    {
        var doc1 = new DocumentBuilder().WithCollection("commit-tests").WithId("commit-multi-1").WithField("v", "a");
        var doc2 = new DocumentBuilder().WithCollection("commit-tests").WithId("commit-multi-2").WithField("v", "b");

        var response = await Client.CommitAsync(
            doc1.BuildCommitRequest(doc1.BuildUpsertWrite(), doc2.BuildUpsertWrite()),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, response.WriteResults.Count);

        var result1 = await Client.GetDocumentAsync(doc1.BuildGetRequest(), cancellationToken: TestContext.Current.CancellationToken);
        var result2 = await Client.GetDocumentAsync(doc2.BuildGetRequest(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("a", result1.Fields["v"].StringValue);
        Assert.Equal("b", result2.Fields["v"].StringValue);
    }

    // ── BatchWrite ────────────────────────────────────────────────────────────

    [Fact]
    public async Task BatchWrite_SuccessfulWrites_AllStatusOk()
    {
        var doc1 = new DocumentBuilder().WithCollection("bw-tests").WithId("bw-ok-1").WithField("v", "1");
        var doc2 = new DocumentBuilder().WithCollection("bw-tests").WithId("bw-ok-2").WithField("v", "2");

        var response = await Client.BatchWriteAsync(
            doc1.BuildBatchWriteRequest(doc1.BuildUpsertWrite(), doc2.BuildUpsertWrite()),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, response.WriteResults.Count);
        Assert.All(response.Status, s => Assert.Equal((int)StatusCode.OK, s.Code));
    }

    [Fact]
    public async Task BatchWrite_MixedResults_ReturnsPerWriteStatus()
    {
        var good = new DocumentBuilder().WithCollection("bw-tests").WithId("bw-mixed-good").WithField("v", "ok");
        var bad = new DocumentBuilder().WithCollection("bw-tests").WithId("bw-mixed-bad");
        var failingWrite = new Write { Update = bad.Build(), CurrentDocument = new Precondition { Exists = true } };

        var request = good.BuildBatchWriteRequest(good.BuildUpsertWrite(), failingWrite);
        var response = await Client.BatchWriteAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, response.Status.Count);
        Assert.Equal((int)StatusCode.OK, response.Status[0].Code);
        Assert.Equal((int)StatusCode.FailedPrecondition, response.Status[1].Code);
    }

    // ── Transactions ──────────────────────────────────────────────────────────

    [Fact]
    public async Task BeginTransaction_ReadWrite_ReturnsTransactionId()
    {
        var builder = new DocumentBuilder();
        var response = await Client.BeginTransactionAsync(
            builder.BuildBeginTransactionRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(response.Transaction);
        Assert.False(response.Transaction.IsEmpty);
    }

    [Fact]
    public async Task BeginTransaction_ReadOnly_ReturnsTransactionId()
    {
        var builder = new DocumentBuilder();
        var options = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() };
        var response = await Client.BeginTransactionAsync(
            builder.BuildBeginTransactionRequest(options),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(response.Transaction);
        Assert.False(response.Transaction.IsEmpty);
    }

    [Fact]
    public async Task BeginTransaction_RetryTransaction_ReturnsNewTransactionId()
    {
        var builder = new DocumentBuilder();

        var first = await Client.BeginTransactionAsync(
            builder.BuildBeginTransactionRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Rollback the first transaction so it's completed
        await Client.RollbackAsync(
            builder.BuildRollbackRequest(first.Transaction),
            cancellationToken: TestContext.Current.CancellationToken);

        // Begin a retry transaction referencing the first
        var retryOptions = new TransactionOptions
        {
            ReadWrite = new TransactionOptions.Types.ReadWrite { RetryTransaction = first.Transaction }
        };
        var second = await Client.BeginTransactionAsync(
            builder.BuildBeginTransactionRequest(retryOptions),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(second.Transaction);
        Assert.False(second.Transaction.IsEmpty);
        Assert.NotEqual(first.Transaction, second.Transaction);
    }

    [Fact]
    public async Task Rollback_ActiveTransaction_Succeeds()
    {
        var builder = new DocumentBuilder();
        var txn = await Client.BeginTransactionAsync(
            builder.BuildBeginTransactionRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Should not throw
        await Client.RollbackAsync(
            builder.BuildRollbackRequest(txn.Transaction),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Rollback_UnknownTransaction_Throws()
    {
        var builder = new DocumentBuilder();
        var fakeId = ByteString.CopyFromUtf8("nonexistent-txn-id");

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            Client.RollbackAsync(
                builder.BuildRollbackRequest(fakeId),
                cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Commit_WithTransaction_AppliesWrites()
    {
        var builder = new DocumentBuilder().WithCollection("txn-tests").WithId("txn-commit-1").WithField("x", "hello");

        var txn = await Client.BeginTransactionAsync(
            builder.BuildBeginTransactionRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        var response = await Client.CommitAsync(
            builder.BuildTransactionalCommitRequest(txn.Transaction, builder.BuildUpsertWrite()),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(response.WriteResults);
        Assert.NotNull(response.CommitTime);

        var doc = await Client.GetDocumentAsync(builder.BuildGetRequest(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("hello", doc.Fields["x"].StringValue);
    }

    [Fact]
    public async Task Commit_WithTransaction_ReadSetConflict_ThrowsAborted()
    {
        // Setup: create a document
        var builder = new DocumentBuilder().WithCollection("txn-tests").WithId("txn-conflict-1").WithField("v", "original");
        await Client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        // Step 1: Begin transaction and read the document (populates read-set)
        var txn = await Client.BeginTransactionAsync(
            builder.BuildBeginTransactionRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        await Client.GetDocumentAsync(
            builder.BuildTransactionalGetRequest(txn.Transaction),
            cancellationToken: TestContext.Current.CancellationToken);

        // Step 2: Modify the document OUTSIDE the transaction
        var outsideUpdate = new DocumentBuilder().WithCollection("txn-tests").WithId("txn-conflict-1").WithField("v", "modified-outside");
        await Client.CommitAsync(
            outsideUpdate.BuildCommitRequest(outsideUpdate.BuildUpsertWrite()),
            cancellationToken: TestContext.Current.CancellationToken);

        // Step 3: Try to commit the transaction — should fail with ABORTED
        var write = new DocumentBuilder().WithCollection("txn-tests").WithId("txn-conflict-1").WithField("v", "txn-value");

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            Client.CommitAsync(
                write.BuildTransactionalCommitRequest(txn.Transaction, write.BuildUpsertWrite()),
                cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        Assert.Equal(StatusCode.Aborted, ex.StatusCode);
    }

    [Fact]
    public async Task Commit_WithTransaction_NoConflict_Succeeds()
    {
        // Setup: create a document
        var builder = new DocumentBuilder().WithCollection("txn-tests").WithId("txn-noconflict-1").WithField("v", "original");
        await Client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        // Begin transaction and read
        var txn = await Client.BeginTransactionAsync(
            builder.BuildBeginTransactionRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        await Client.GetDocumentAsync(
            builder.BuildTransactionalGetRequest(txn.Transaction),
            cancellationToken: TestContext.Current.CancellationToken);

        // No external modification

        // Commit with write — should succeed
        var update = new DocumentBuilder().WithCollection("txn-tests").WithId("txn-noconflict-1").WithField("v", "updated-in-txn");
        var response = await Client.CommitAsync(
            update.BuildTransactionalCommitRequest(txn.Transaction, update.BuildUpsertWrite()),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(response.WriteResults);

        var doc = await Client.GetDocumentAsync(builder.BuildGetRequest(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("updated-in-txn", doc.Fields["v"].StringValue);
    }

    [Fact]
    public async Task Commit_WithoutTransaction_AtomicAllOrNothing()
    {
        // Create a document that will cause the SECOND write to fail via precondition
        var existing = new DocumentBuilder().WithCollection("txn-tests").WithId("atomic-existing").WithField("v", "exists");
        await Client.CreateDocumentAsync(existing.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        // First write: create a new document
        var newDoc = new DocumentBuilder().WithCollection("txn-tests").WithId("atomic-new").WithField("v", "new");
        var write1 = newDoc.BuildUpsertWrite();

        // Second write: precondition requires doc NOT to exist, but it does → fails
        var write2 = new Write { Update = existing.Build(), CurrentDocument = new Precondition { Exists = false } };

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            Client.CommitAsync(
                newDoc.BuildCommitRequest(write1, write2),
                cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);

        // The first write should NOT have been applied (atomic rollback)
        var getEx = await Assert.ThrowsAsync<RpcException>(() =>
            Client.GetDocumentAsync(newDoc.BuildGetRequest(), cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);
        Assert.Equal(StatusCode.NotFound, getEx.StatusCode);
    }

    [Fact]
    public async Task Commit_ReadOnlyTransaction_WithWrites_Throws()
    {
        var builder = new DocumentBuilder().WithCollection("txn-tests").WithId("readonly-write-1").WithField("x", "y");

        var options = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() };
        var txn = await Client.BeginTransactionAsync(
            builder.BuildBeginTransactionRequest(options),
            cancellationToken: TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            Client.CommitAsync(
                builder.BuildTransactionalCommitRequest(txn.Transaction, builder.BuildUpsertWrite()),
                cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Commit_ReadOnlyTransaction_NoWrites_Succeeds()
    {
        var builder = new DocumentBuilder().WithCollection("txn-tests").WithId("readonly-nowrite-1").WithField("x", "y");
        await Client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var options = new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() };
        var txn = await Client.BeginTransactionAsync(
            builder.BuildBeginTransactionRequest(options),
            cancellationToken: TestContext.Current.CancellationToken);

        // Read within transaction
        await Client.GetDocumentAsync(
            builder.BuildTransactionalGetRequest(txn.Transaction),
            cancellationToken: TestContext.Current.CancellationToken);

        // Commit with no writes — should succeed
        var response = await Client.CommitAsync(
            builder.BuildTransactionalCommitRequest(txn.Transaction),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(response.CommitTime);
    }

    [Fact]
    public async Task Commit_TransactionAlreadyCommitted_Throws()
    {
        var builder = new DocumentBuilder().WithCollection("txn-tests").WithId("double-commit-1").WithField("x", "y");

        var txn = await Client.BeginTransactionAsync(
            builder.BuildBeginTransactionRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        // First commit succeeds
        await Client.CommitAsync(
            builder.BuildTransactionalCommitRequest(txn.Transaction, builder.BuildUpsertWrite()),
            cancellationToken: TestContext.Current.CancellationToken);

        // Second commit on same transaction should fail
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            Client.CommitAsync(
                builder.BuildTransactionalCommitRequest(txn.Transaction, builder.BuildUpsertWrite()),
                cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Commit_ExceedsMaxWrites_ThrowsInvalidArgument()
    {
        var builder = new DocumentBuilder().WithCollection("txn-tests");
        var writes = Enumerable.Range(0, 501)
            .Select(i => new DocumentBuilder()
                .WithCollection("txn-tests")
                .WithId($"over-limit-{i}")
                .WithField("i", (long)i)
                .BuildUpsertWrite())
            .ToArray();

        var request = builder.BuildCommitRequest(writes);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            Client.CommitAsync(request, cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.Contains("A transaction cannot contain more than 500 writes.", ex.Status.Detail, StringComparison.Ordinal);
    }
}
