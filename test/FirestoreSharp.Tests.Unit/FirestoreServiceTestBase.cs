using Google.Cloud.Firestore.V1;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FirestoreSharp.Tests.Unit;

public abstract class FirestoreServiceTestBase : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly GrpcChannel _channel;
    protected readonly Firestore.FirestoreClient Client;

    protected FirestoreServiceTestBase(WebApplicationFactory<Program> factory)
    {
        var httpClient = factory.CreateDefaultClient();
        _channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });
        Client = new Firestore.FirestoreClient(_channel);
    }

    public void Dispose() => _channel.Dispose();

    /// <summary>
    /// Drains the initial snapshot sequence (ADD, optional document changes, CURRENT,
    /// optional ExistenceFilter, NO_CHANGE) so tests can proceed to live-update assertions.
    /// </summary>
    protected static async Task DrainInitialSnapshotAsync(
        AsyncDuplexStreamingCall<ListenRequest, ListenResponse> call,
        CancellationToken ct)
    {
        while (true)
        {
            var response = await ReadNextAsync(call, ct);
            if (response is
                {
                    ResponseTypeCase: ListenResponse.ResponseTypeOneofCase.TargetChange,
                    TargetChange.TargetChangeType: TargetChange.Types.TargetChangeType.NoChange,
                })
            {
                return;
            }
        }
    }

    protected static async Task<ListenResponse> ReadNextAsync(
        AsyncDuplexStreamingCall<ListenRequest, ListenResponse> call,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        Assert.True(await call.ResponseStream.MoveNext(cts.Token));
        return call.ResponseStream.Current;
    }

    protected static async Task AssertNoMoreResponsesAsync(
        AsyncDuplexStreamingCall<ListenRequest, ListenResponse> call,
        CancellationToken ct)
    {
        while (true)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(200));
            try
            {
                var hasMore = await call.ResponseStream.MoveNext(cts.Token);
                if (!hasMore)
                {
                    return;
                }
                // Skip protocol-level snapshot signals — not application data.
                var current = call.ResponseStream.Current;
                if (current is
                    {
                        ResponseTypeCase: ListenResponse.ResponseTypeOneofCase.TargetChange,
                        TargetChange.TargetChangeType: TargetChange.Types.TargetChangeType.NoChange,
                    })
                {
                    continue;
                }
                if (current.ResponseTypeCase == ListenResponse.ResponseTypeOneofCase.Filter)
                {
                    continue;
                }
                Assert.Fail($"Expected no more responses but received: {current}");
            }
            catch (OperationCanceledException)
            {
                // expected — no more responses
                return;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                // gRPC wraps cancellation
                return;
            }
        }
    }
}
