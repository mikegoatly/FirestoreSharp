using System.Threading.Channels;

using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core.Listeners;

/// <summary>
/// Represents a single Listen RPC bidirectional stream.
/// The gRPC layer reads from <see cref="Responses"/> and writes them to the client.
/// </summary>
public interface IListenerConnection : IAsyncDisposable
{
    /// <summary>
    /// Outbound responses to be sent to the client.
    /// </summary>
    ChannelReader<ListenResponse> Responses { get; }

    /// <summary>
    /// Registers a new target (document watch or query watch), sends the initial snapshot,
    /// and begins tracking changes for the target.
    /// </summary>
    Task AddTargetAsync(Target target, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters a target and sends a <see cref="TargetChange"/> REMOVE notification.
    /// </summary>
    void RemoveTarget(int targetId);
}
