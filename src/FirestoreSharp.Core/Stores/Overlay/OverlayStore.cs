using System.Runtime.CompilerServices;

using Google.Cloud.Firestore.V1;

using Grpc.Core;

namespace FirestoreSharp.Core.Stores.Overlay;

/// <summary>
/// A copy-on-write overlay over a base <see cref="IDocumentStore"/>.
///
/// Used to give each read-write transaction an isolated view of the document store:
/// - Reads fall through to the base store on first access and are promoted into the overlay.
/// - Writes are buffered in the overlay and never touch the base store.
/// - At commit time, only dirty (written) entries need to be applied to the base store —
///   the existing <see cref="DocumentService"/> apply phase handles this naturally when
///   it prepares mutations against this overlay.
///
/// Limitations (by design — see README):
/// - Snapshot time is per-document, not global. Two documents read at different points
///   during the same transaction may reflect different moments in time.
/// - Write skew is not detected.
/// </summary>
internal sealed class OverlayStore(IDocumentStore baseStore) : IDocumentStore
{
    private readonly Dictionary<string, OverlayEntry> _overlay = new(StringComparer.Ordinal);

    // ── Reads ─────────────────────────────────────────────────────────────────

    public async Task<Document> GetAsync(DocumentPath path, CancellationToken cancellationToken = default)
    {
        var entry = GetOverlayEntry(path.ResourceName);

        if (entry is not null)
        {
            if (entry.IsDeleted)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"Document not found: {path.ResourceName}"));
            }

            return entry.Document!.Clone();
        }

        var document = await baseStore.GetAsync(path, cancellationToken).ConfigureAwait(false);
        Promote(path.ResourceName, document);
        return document.Clone();
    }

    public async Task<Document?> TryGetAsync(DocumentPath path, CancellationToken cancellationToken = default)
    {
        var entry = GetOverlayEntry(path.ResourceName);

        if (entry is not null)
        {
            return entry.IsDeleted ? null : entry.Document!.Clone();
        }

        var document = await baseStore.TryGetAsync(path, cancellationToken).ConfigureAwait(false);

        if (document is not null)
        {
            Promote(path.ResourceName, document);
            return document.Clone();
        }

        // Promote the miss so repeated reads of a missing doc stay consistent.
        PromoteMiss(path.ResourceName);
        return null;
    }

    public async IAsyncEnumerable<Document> ListAsync(
        ReadOnlyMemory<char> parentPrefix,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Collect base documents, applying overlay on top.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var document in baseStore.ListAsync(parentPrefix, cancellationToken).ConfigureAwait(false))
        {
            seen.Add(document.Name);
            var entry = GetOverlayEntry(document.Name);

            if (entry is null)
            {
                // Not in overlay — promote and yield as-is.
                Promote(document.Name, document);
                yield return document.Clone();
            }
            else if (!entry.IsDeleted)
            {
                // Overlay version wins.
                yield return entry.Document!.Clone();
            }
            // else: tombstoned — skip
        }

        // Yield overlay-only documents (created in this transaction, not yet in base).
        foreach (var (resourceName, entry) in _overlay)
        {
            if (!seen.Contains(resourceName)
                && resourceName.AsSpan().StartsWith(parentPrefix.Span, StringComparison.Ordinal)
                && !entry.IsDeleted
                && entry.IsDirty)
            {
                yield return entry.Document!.Clone();
            }
        }
    }

    // ── Writes (buffered to overlay only) ─────────────────────────────────────

    public async Task CreateAsync(DocumentPath path, Document document, CancellationToken cancellationToken = default)
    {
        // Check overlay first (fast path — no I/O needed if we already know it exists).
        var entry = GetOverlayEntry(path.ResourceName);

        if (entry is not null && !entry.IsDeleted)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, $"Document already exists: {path.ResourceName}"));
        }

        if (entry is null)
        {
            // Check base store to enforce AlreadyExists semantics.
            var existing = await baseStore.TryGetAsync(path, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                throw new RpcException(new Status(StatusCode.AlreadyExists, $"Document already exists: {path.ResourceName}"));
            }
        }

        WriteToOverlay(path.ResourceName, document);
    }

    public async Task<Document> UpdateAsync(DocumentPath path, Document document, CancellationToken cancellationToken = default)
    {
        var entry = GetOverlayEntry(path.ResourceName);

        if (entry is null)
        {
            // Ensure the document exists in the base store before updating.
            var existing = await baseStore.TryGetAsync(path, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"Document not found: {path.ResourceName}"));
            }
        }
        else if (entry.IsDeleted)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Document not found: {path.ResourceName}"));
        }

        WriteToOverlay(path.ResourceName, document);
        return document.Clone();
    }

    public async Task DeleteAsync(DocumentPath path, CancellationToken cancellationToken = default)
    {
        var entry = GetOverlayEntry(path.ResourceName);

        if (entry is null)
        {
            var existing = await baseStore.TryGetAsync(path, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"Document not found: {path.ResourceName}"));
            }
        }
        else if (entry.IsDeleted)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Document not found: {path.ResourceName}"));
        }

        Tombstone(path.ResourceName);
    }

    public Task<IReadOnlyList<(string Project, string Database)>> GetKnownDatabasesAsync(CancellationToken cancellationToken = default) =>
        baseStore.GetKnownDatabasesAsync(cancellationToken);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private OverlayEntry? GetOverlayEntry(string resourceName) =>
        _overlay.TryGetValue(resourceName, out var entry) ? entry : null;

    private void Promote(string resourceName, Document document) =>
        _overlay[resourceName] = new OverlayEntry(document.Clone(), IsDeleted: false, IsDirty: false);

    private void PromoteMiss(string resourceName) =>
        _overlay.TryAdd(resourceName, new OverlayEntry(Document: null, IsDeleted: true, IsDirty: false));

    private void WriteToOverlay(string resourceName, Document document) =>
        _overlay[resourceName] = new OverlayEntry(document.Clone(), IsDeleted: false, IsDirty: true);

    private void Tombstone(string resourceName) =>
        _overlay[resourceName] = new OverlayEntry(Document: null, IsDeleted: true, IsDirty: true);
}
