using FirestoreSharp.Core;
using FirestoreSharp.Core.Stores.InMemory;
using FirestoreSharp.Core.Stores.Overlay;

using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Xunit;

namespace FirestoreSharp.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="OverlayStore"/>.
/// </summary>
public sealed class OverlayStoreTests
{
    private const string Parent = "projects/test/databases/(default)/documents";
    private const string Collection = "col";

    private static DocumentPath MakePath(string id) =>
        DocumentPath.FromCreateRequest(Parent, Collection, id);

    private static Document MakeDocument(string id, string fieldValue)
    {
        var doc = new Document
        {
            Name = $"{Parent}/{Collection}/{id}",
            CreateTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            UpdateTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        };
        doc.Fields["v"] = new Google.Cloud.Firestore.V1.Value { StringValue = fieldValue };
        return doc;
    }

    private static (InMemoryDocumentStore Base, OverlayStore Overlay) MakeStores()
    {
        var baseStore = new InMemoryDocumentStore();
        var overlay = new OverlayStore(baseStore);
        return (baseStore, overlay);
    }

    // ── Read promotion ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_DocumentInBase_ReturnsDocument()
    {
        var ct = TestContext.Current.CancellationToken;
        var (baseStore, overlay) = MakeStores();
        var path = MakePath("doc1");
        await baseStore.CreateAsync(path, MakeDocument("doc1", "hello"), ct);

        var result = await overlay.GetAsync(path, ct);

        Assert.Equal("hello", result.Fields["v"].StringValue);
    }

    [Fact]
    public async Task GetAsync_DocumentReadTwice_ReturnsSameVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        var (baseStore, overlay) = MakeStores();
        var path = MakePath("doc1");
        await baseStore.CreateAsync(path, MakeDocument("doc1", "original"), ct);

        // First read — promotes into overlay
        var first = await overlay.GetAsync(path, ct);

        // Modify base store externally (simulates another transaction committing)
        await baseStore.UpdateAsync(path, MakeDocument("doc1", "modified"), ct);

        // Second read — should return overlay version (original), not base version
        var second = await overlay.GetAsync(path, ct);

        Assert.Equal("original", first.Fields["v"].StringValue);
        Assert.Equal("original", second.Fields["v"].StringValue);
    }

    [Fact]
    public async Task TryGetAsync_MissingDocument_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, overlay) = MakeStores();
        var path = MakePath("missing");

        var result = await overlay.TryGetAsync(path, ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryGetAsync_MissingDocumentReadTwice_ReturnNullBothTimes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (baseStore, overlay) = MakeStores();
        var path = MakePath("doc1");

        // First read — promotes miss into overlay
        var first = await overlay.TryGetAsync(path, ct);

        // Document created in base externally
        await baseStore.CreateAsync(path, MakeDocument("doc1", "new"), ct);

        // Second read — overlay miss should be stable
        var second = await overlay.TryGetAsync(path, ct);

        Assert.Null(first);
        Assert.Null(second);
    }

    // ── Write isolation ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WrittenToOverlay_NotVisibleInBase()
    {
        var ct = TestContext.Current.CancellationToken;
        var (baseStore, overlay) = MakeStores();
        var path = MakePath("doc1");
        await overlay.CreateAsync(path, MakeDocument("doc1", "overlay-value"), ct);

        var inOverlay = await overlay.TryGetAsync(path, ct);
        var inBase = await baseStore.TryGetAsync(path, ct);

        Assert.NotNull(inOverlay);
        Assert.Equal("overlay-value", inOverlay!.Fields["v"].StringValue);
        Assert.Null(inBase);
    }

    [Fact]
    public async Task UpdateAsync_WrittenToOverlay_NotVisibleInBase()
    {
        var ct = TestContext.Current.CancellationToken;
        var (baseStore, overlay) = MakeStores();
        var path = MakePath("doc1");
        await baseStore.CreateAsync(path, MakeDocument("doc1", "original"), ct);

        await overlay.UpdateAsync(path, MakeDocument("doc1", "updated"), ct);

        var inOverlay = await overlay.TryGetAsync(path, ct);
        var inBase = await baseStore.TryGetAsync(path, ct);

        Assert.Equal("updated", inOverlay!.Fields["v"].StringValue);
        Assert.Equal("original", inBase!.Fields["v"].StringValue);
    }

    [Fact]
    public async Task CreateAsync_AlreadyExistsInBase_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var (baseStore, overlay) = MakeStores();
        var path = MakePath("doc1");
        await baseStore.CreateAsync(path, MakeDocument("doc1", "original"), ct);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            overlay.CreateAsync(path, MakeDocument("doc1", "duplicate"), ct));

        Assert.Equal(StatusCode.AlreadyExists, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_DocumentNotFoundInBaseOrOverlay_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, overlay) = MakeStores();
        var path = MakePath("missing");

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            overlay.UpdateAsync(path, MakeDocument("missing", "value"), ct));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    // ── Tombstones ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_DocumentInBase_TombstonedInOverlay_NotVisibleViaOverlay()
    {
        var ct = TestContext.Current.CancellationToken;
        var (baseStore, overlay) = MakeStores();
        var path = MakePath("doc1");
        await baseStore.CreateAsync(path, MakeDocument("doc1", "value"), ct);

        await overlay.DeleteAsync(path, ct);

        var inOverlay = await overlay.TryGetAsync(path, ct);
        Assert.Null(inOverlay);

        // Base should still have the document
        var inBase = await baseStore.TryGetAsync(path, ct);
        Assert.NotNull(inBase);
    }

    [Fact]
    public async Task GetAsync_TombstonedDocument_ThrowsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var (baseStore, overlay) = MakeStores();
        var path = MakePath("doc1");
        await baseStore.CreateAsync(path, MakeDocument("doc1", "value"), ct);
        await overlay.DeleteAsync(path, ct);

        var ex = await Assert.ThrowsAsync<RpcException>(() => overlay.GetAsync(path, ct));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_DocumentNotFound_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, overlay) = MakeStores();
        var path = MakePath("missing");

        var ex = await Assert.ThrowsAsync<RpcException>(() => overlay.DeleteAsync(path, ct));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_AlreadyTombstoned_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var (baseStore, overlay) = MakeStores();
        var path = MakePath("doc1");
        await baseStore.CreateAsync(path, MakeDocument("doc1", "value"), ct);
        await overlay.DeleteAsync(path, ct);

        var ex = await Assert.ThrowsAsync<RpcException>(() => overlay.DeleteAsync(path, ct));
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    // ── ListAsync merging ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_BaseAndOverlayMerged_AllVisible()
    {
        var ct = TestContext.Current.CancellationToken;
        var (baseStore, overlay) = MakeStores();
        var prefix = $"{Parent}/{Collection}/";

        await baseStore.CreateAsync(MakePath("doc1"), MakeDocument("doc1", "base1"), ct);
        await baseStore.CreateAsync(MakePath("doc2"), MakeDocument("doc2", "base2"), ct);
        // doc3 is overlay-only (created in transaction, not yet in base)
        await overlay.CreateAsync(MakePath("doc3"), MakeDocument("doc3", "overlay3"), ct);

        var results = await overlay.ListAsync(prefix.AsMemory(), ct).ToListAsync();

        Assert.Equal(3, results.Count);
        Assert.Contains(results, d => d.Fields["v"].StringValue == "base1");
        Assert.Contains(results, d => d.Fields["v"].StringValue == "base2");
        Assert.Contains(results, d => d.Fields["v"].StringValue == "overlay3");
    }

    [Fact]
    public async Task ListAsync_OverlayUpdateWins_OverBaseVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        var (baseStore, overlay) = MakeStores();
        var prefix = $"{Parent}/{Collection}/";
        var path = MakePath("doc1");

        await baseStore.CreateAsync(path, MakeDocument("doc1", "original"), ct);
        await overlay.UpdateAsync(path, MakeDocument("doc1", "updated"), ct);

        var results = await overlay.ListAsync(prefix.AsMemory(), ct).ToListAsync();

        Assert.Single(results);
        Assert.Equal("updated", results[0].Fields["v"].StringValue);
    }

    [Fact]
    public async Task ListAsync_TombstonedDocument_Excluded()
    {
        var ct = TestContext.Current.CancellationToken;
        var (baseStore, overlay) = MakeStores();
        var prefix = $"{Parent}/{Collection}/";

        await baseStore.CreateAsync(MakePath("doc1"), MakeDocument("doc1", "keep"), ct);
        await baseStore.CreateAsync(MakePath("doc2"), MakeDocument("doc2", "delete-me"), ct);
        await overlay.DeleteAsync(MakePath("doc2"), ct);

        var results = await overlay.ListAsync(prefix.AsMemory(), ct).ToListAsync();

        Assert.Single(results);
        Assert.Equal("keep", results[0].Fields["v"].StringValue);
    }

    // ── IsDirty / read-promotion idempotency ───────────────────────────────────

    [Fact]
    public async Task ReadPromotion_DocumentNotDuplicated_InListAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var (baseStore, overlay) = MakeStores();
        var prefix = $"{Parent}/{Collection}/";

        await baseStore.CreateAsync(MakePath("doc1"), MakeDocument("doc1", "base"), ct);

        // Promote doc1 by reading it — should not cause it to appear twice in ListAsync
        await overlay.GetAsync(MakePath("doc1"), ct);

        var results = await overlay.ListAsync(prefix.AsMemory(), ct).ToListAsync();

        Assert.Single(results);
        Assert.Equal("base", results[0].Fields["v"].StringValue);
    }

    [Fact]
    public async Task WriteAfterRead_DocumentReflectsWrite_InListAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var (baseStore, overlay) = MakeStores();
        var prefix = $"{Parent}/{Collection}/";
        var path = MakePath("doc1");

        await baseStore.CreateAsync(path, MakeDocument("doc1", "original"), ct);

        // Read (promotes) then write within the same transaction
        await overlay.GetAsync(path, ct);
        await overlay.UpdateAsync(path, MakeDocument("doc1", "written"), ct);

        var results = await overlay.ListAsync(prefix.AsMemory(), ct).ToListAsync();

        Assert.Single(results);
        Assert.Equal("written", results[0].Fields["v"].StringValue);
    }
}

file static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }
}
