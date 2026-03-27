namespace FirestoreSharp.Core.Listeners;

/// <summary>
/// Manages the set of active listener connections and dispatches document-change
/// notifications to all of them.
/// </summary>
public interface IListenerService : IDocumentChangeNotifier
{
    /// <summary>
    /// Creates a new listener connection that can register targets and receive change notifications.
    /// The caller is responsible for disposing the connection when the Listen RPC stream ends.
    /// </summary>
    IListenerConnection CreateConnection();
}
