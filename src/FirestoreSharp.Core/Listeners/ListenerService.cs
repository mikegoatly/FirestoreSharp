using System.Collections.Concurrent;

namespace FirestoreSharp.Core.Listeners;

internal sealed class ListenerService(IDocumentStore store) : IListenerService
{
    private readonly ConcurrentDictionary<Guid, ListenerConnection> _connections = new();

    public IListenerConnection CreateConnection()
    {
        var id = Guid.NewGuid();
        var connection = new ListenerConnection(store, () => _connections.TryRemove(id, out _));
        _connections[id] = connection;
        return connection;
    }

    public void NotifyDocumentsChanged(IReadOnlyList<DocumentMutation> mutations)
    {
        foreach (var connection in _connections.Values)
        {
            connection.ProcessMutations(mutations);
        }
    }
}
