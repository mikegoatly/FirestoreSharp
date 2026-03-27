using System.Threading.Channels;

using FirestoreSharp.Core.Query;

using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;

namespace FirestoreSharp.Core.Listeners;

internal sealed class ListenerConnection(IDocumentStore store, Action onDisposed) : IListenerConnection
{
    private readonly Channel<ListenResponse> _channel = Channel.CreateUnbounded<ListenResponse>();
    private readonly Dictionary<int, ListenerTarget> _targets = [];
    private readonly Lock _lock = new();
    private int _nextTargetId;

    public ChannelReader<ListenResponse> Responses => _channel.Reader;

    public async Task AddTargetAsync(Target target, CancellationToken cancellationToken = default)
    {
        var targetId = target.TargetId != 0 ? target.TargetId : Interlocked.Increment(ref _nextTargetId);

        ListenerTarget listenerTarget = target.TargetTypeCase switch
        {
            Target.TargetTypeOneofCase.Documents =>
                new DocumentListenerTarget(targetId, [.. target.Documents.Documents]),
            Target.TargetTypeOneofCase.Query =>
                new QueryListenerTarget(targetId, target.Query.Parent, target.Query.StructuredQuery),
            _ => throw new ArgumentException($"Unsupported target type: {target.TargetTypeCase}", nameof(target)),
        };

        lock (_lock)
        {
            _targets[targetId] = listenerTarget;
        }

        // Send the initial snapshot — ADD first, then document changes, then CURRENT.
        SendTargetChange(TargetChange.Types.TargetChangeType.Add, targetId);

        await SendInitialSnapshotAsync(listenerTarget, cancellationToken).ConfigureAwait(false);

        SendTargetChange(TargetChange.Types.TargetChangeType.Current, targetId);
        SendNoChange();
    }

    public void RemoveTarget(int targetId)
    {
        lock (_lock)
        {
            _targets.Remove(targetId);
        }

        SendTargetChange(TargetChange.Types.TargetChangeType.Remove, targetId);
    }

    /// <summary>
    /// Called by <see cref="ListenerService"/> when documents are mutated.
    /// Evaluates each mutation against all registered targets and sends appropriate notifications.
    /// </summary>
    internal void ProcessMutations(IReadOnlyList<DocumentMutation> mutations)
    {
        var sentAny = false;
        lock (_lock)
        {
            foreach (var mutation in mutations)
            {
                foreach (var target in _targets.Values)
                {
                    if (ProcessMutationForTarget(target, mutation))
                    {
                        sentAny = true;
                    }
                }
            }
        }

        // Signal the client that this batch is complete. The official SDK
        // buffers document changes and only delivers a snapshot when it
        // receives NO_CHANGE (with a read_time and empty target_ids).
        // Only send it when we actually wrote something to avoid spurious wakeups.
        if (sentAny)
        {
            SendNoChange();
        }
    }

    public ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        onDisposed();
        return ValueTask.CompletedTask;
    }

    // ── Initial snapshot ──────────────────────────────────────────────────────

    private async Task SendInitialSnapshotAsync(ListenerTarget target, CancellationToken cancellationToken)
    {
        switch (target)
        {
            case DocumentListenerTarget docTarget:
                await SendDocumentTargetSnapshotAsync(docTarget, cancellationToken).ConfigureAwait(false);
                break;

            case QueryListenerTarget queryTarget:
                await SendQueryTargetSnapshotAsync(queryTarget, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task SendDocumentTargetSnapshotAsync(DocumentListenerTarget target, CancellationToken cancellationToken)
    {
        foreach (var resourceName in target.DocumentNames)
        {
            var path = DocumentPath.Parse(resourceName);
            var doc = await store.TryGetAsync(path, cancellationToken).ConfigureAwait(false);

            if (doc is not null)
            {
                target.ActiveDocuments.Add(resourceName);
                SendDocumentChange(doc, [target.TargetId], []);
            }
        }
    }

    private async Task SendQueryTargetSnapshotAsync(QueryListenerTarget target, CancellationToken cancellationToken)
    {
        var candidates = new List<Document>();
        await foreach (var doc in store.ListAsync(target.Parent, cancellationToken).ConfigureAwait(false))
        {
            candidates.Add(doc);
        }

        var results = QueryEngine.Execute(target.Parent, target.Query, candidates);

        foreach (var doc in results)
        {
            target.ActiveDocuments.Add(doc.Name);
            SendDocumentChange(doc, [target.TargetId], []);
        }
    }

    // ── Live mutation processing ──────────────────────────────────────────────

    private bool ProcessMutationForTarget(ListenerTarget target, DocumentMutation mutation)
    {
        return target switch
        {
            DocumentListenerTarget docTarget => ProcessDocumentTargetMutation(docTarget, mutation),
            QueryListenerTarget queryTarget => ProcessQueryTargetMutation(queryTarget, mutation),
            _ => false,
        };
    }

    private bool ProcessDocumentTargetMutation(DocumentListenerTarget target, DocumentMutation mutation)
    {
        // Only interested in documents this target is watching.
        var isWatched = false;
        foreach (var name in target.DocumentNames)
        {
            if (string.Equals(name, mutation.ResourceName, StringComparison.Ordinal))
            {
                isWatched = true;
                break;
            }
        }

        if (!isWatched)
        {
            return false;
        }

        var wasActive = target.ActiveDocuments.Contains(mutation.ResourceName);

        if (mutation.NewState is not null)
        {
            // Created or updated — send DocumentChange.
            target.ActiveDocuments.Add(mutation.ResourceName);
            SendDocumentChange(mutation.NewState, [target.TargetId], []);
            return true;
        }
        else if (wasActive)
        {
            // Deleted — send DocumentDelete.
            target.ActiveDocuments.Remove(mutation.ResourceName);
            SendDocumentDelete(mutation.ResourceName, [target.TargetId]);
            return true;
        }

        return false;
    }

    private bool ProcessQueryTargetMutation(QueryListenerTarget target, DocumentMutation mutation)
    {
        var wasActive = target.ActiveDocuments.Contains(mutation.ResourceName);
        var matchesNow = mutation.NewState is not null && MatchesQuery(target, mutation.NewState);

        switch (wasActive, matchesNow)
        {
            case (false, true):
                // New document enters the query result set.
                target.ActiveDocuments.Add(mutation.ResourceName);
                SendDocumentChange(mutation.NewState!, [target.TargetId], []);
                return true;

            case (true, true):
                // Document still matches — send updated state.
                SendDocumentChange(mutation.NewState!, [target.TargetId], []);
                return true;

            case (true, false) when mutation.NewState is null:
                // Document was deleted.
                target.ActiveDocuments.Remove(mutation.ResourceName);
                SendDocumentDelete(mutation.ResourceName, [target.TargetId]);
                return true;

            case (true, false):
                // Document still exists but no longer matches the query — send DocumentRemove.
                target.ActiveDocuments.Remove(mutation.ResourceName);
                SendDocumentRemove(mutation.ResourceName, [target.TargetId]);
                return true;

            default:
                // Not relevant to this target — nothing to do.
                return false;
        }
    }

    private static bool MatchesQuery(QueryListenerTarget target, Document document)
    {
        // Check collection membership first.
        var fromCollections = target.Query.From;
        if (fromCollections.Count > 0 && !QueryEngine.MatchesCollection(document, target.Parent, fromCollections))
        {
            return false;
        }

        // Then check the where clause.
        if (target.Query.Where is { FilterTypeCase: not StructuredQuery.Types.Filter.FilterTypeOneofCase.None })
        {
            return QueryFilter.Matches(document, target.Query.Where);
        }

        return true;
    }

    // ── Response helpers ──────────────────────────────────────────────────────

    private void SendNoChange()
    {
        var change = new TargetChange
        {
            TargetChangeType = TargetChange.Types.TargetChangeType.NoChange,
            ReadTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        };
        // Empty TargetIds = applies to all targets.
        _channel.Writer.TryWrite(new ListenResponse { TargetChange = change });
    }

    private void SendTargetChange(TargetChange.Types.TargetChangeType changeType, int targetId)
    {
        var change = new TargetChange { TargetChangeType = changeType };
        change.TargetIds.Add(targetId);
        change.ReadTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        _channel.Writer.TryWrite(new ListenResponse { TargetChange = change });
    }

    private void SendDocumentChange(Document document, IReadOnlyList<int> targetIds, IReadOnlyList<int> removedTargetIds)
    {
        var change = new DocumentChange { Document = document };
        change.TargetIds.Add(targetIds);
        change.RemovedTargetIds.Add(removedTargetIds);

        _channel.Writer.TryWrite(new ListenResponse { DocumentChange = change });
    }

    private void SendDocumentDelete(string resourceName, IReadOnlyList<int> removedTargetIds)
    {
        var delete = new DocumentDelete
        {
            Document = resourceName,
            ReadTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        };
        delete.RemovedTargetIds.Add(removedTargetIds);

        _channel.Writer.TryWrite(new ListenResponse { DocumentDelete = delete });
    }

    private void SendDocumentRemove(string resourceName, IReadOnlyList<int> removedTargetIds)
    {
        var remove = new DocumentRemove
        {
            Document = resourceName,
            ReadTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
        };
        remove.RemovedTargetIds.Add(removedTargetIds);

        _channel.Writer.TryWrite(new ListenResponse { DocumentRemove = remove });
    }
}
