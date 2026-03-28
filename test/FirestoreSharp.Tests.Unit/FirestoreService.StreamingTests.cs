using FirestoreSharp.Tests.Unit.Builders;
using Google.Cloud.Firestore.V1;
using Grpc.Core;
using Xunit;

using Microsoft.AspNetCore.Mvc.Testing;

namespace FirestoreSharp.Tests.Unit;

public sealed class FirestoreServiceStreamingTests(WebApplicationFactory<Program> factory) : FirestoreServiceTestBase(factory)
{

    [Fact]
    public async Task Write_Handshake_ReceivesStreamIdAndToken()
    {
        var builder = new DocumentBuilder();
        using var call = Client.Write(cancellationToken: TestContext.Current.CancellationToken);

        await call.RequestStream.WriteAsync(builder.BuildWriteHandshake(), TestContext.Current.CancellationToken);
        await call.RequestStream.CompleteAsync();

        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        var handshakeResponse = call.ResponseStream.Current;

        Assert.False(string.IsNullOrEmpty(handshakeResponse.StreamId));
        Assert.False(handshakeResponse.StreamToken.IsEmpty);
        Assert.Null(handshakeResponse.CommitTime);
        Assert.Empty(handshakeResponse.WriteResults);
    }

    [Fact]
    public async Task Write_SingleBatch_CommitsAndResponds()
    {
        var builder = new DocumentBuilder().WithCollection("ws-tests").WithId("ws-single-1").WithField("v", "hello");
        using var call = Client.Write(cancellationToken: TestContext.Current.CancellationToken);

        // Handshake
        await call.RequestStream.WriteAsync(builder.BuildWriteHandshake(), TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        _ = call.ResponseStream.Current; // consume handshake response

        // Send a write batch
        var writeRequest = new WriteRequest();
        writeRequest.Writes.Add(builder.BuildUpsertWrite());
        await call.RequestStream.WriteAsync(writeRequest, TestContext.Current.CancellationToken);

        // Read the commit response
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        var commitResponse = call.ResponseStream.Current;

        Assert.False(commitResponse.StreamToken.IsEmpty);
        Assert.NotNull(commitResponse.CommitTime);
        Assert.Single(commitResponse.WriteResults);
        Assert.NotNull(commitResponse.WriteResults[0].UpdateTime);

        await call.RequestStream.CompleteAsync();

        // Document should now exist
        var doc = await Client.GetDocumentAsync(builder.BuildGetRequest(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("hello", doc.Fields["v"].StringValue);
    }

    [Fact]
    public async Task Write_MultipleBatches_EachCommittedInOrder()
    {
        var builder = new DocumentBuilder().WithCollection("ws-tests");
        using var call = Client.Write(cancellationToken: TestContext.Current.CancellationToken);

        // Handshake
        await call.RequestStream.WriteAsync(builder.BuildWriteHandshake(), TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        // First batch: create doc1
        var doc1 = new DocumentBuilder().WithCollection("ws-tests").WithId("ws-multi-1").WithField("v", "batch1");
        var req1 = new WriteRequest();
        req1.Writes.Add(doc1.BuildUpsertWrite());
        await call.RequestStream.WriteAsync(req1, TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        var resp1 = call.ResponseStream.Current;
        Assert.Single(resp1.WriteResults);

        // Second batch: create doc2
        var doc2 = new DocumentBuilder().WithCollection("ws-tests").WithId("ws-multi-2").WithField("v", "batch2");
        var req2 = new WriteRequest();
        req2.Writes.Add(doc2.BuildUpsertWrite());
        await call.RequestStream.WriteAsync(req2, TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        var resp2 = call.ResponseStream.Current;
        Assert.Single(resp2.WriteResults);

        await call.RequestStream.CompleteAsync();

        // Both documents must exist
        var result1 = await Client.GetDocumentAsync(doc1.BuildGetRequest(), cancellationToken: TestContext.Current.CancellationToken);
        var result2 = await Client.GetDocumentAsync(doc2.BuildGetRequest(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("batch1", result1.Fields["v"].StringValue);
        Assert.Equal("batch2", result2.Fields["v"].StringValue);
    }

    [Fact]
    public async Task Write_EmptyWritesAfterHandshake_HeartbeatResponse()
    {
        var builder = new DocumentBuilder();
        using var call = Client.Write(cancellationToken: TestContext.Current.CancellationToken);

        // Handshake
        await call.RequestStream.WriteAsync(builder.BuildWriteHandshake(), TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        var firstToken = call.ResponseStream.Current.StreamToken;

        // Send empty writes (heartbeat)
        await call.RequestStream.WriteAsync(new WriteRequest(), TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        var heartbeatResponse = call.ResponseStream.Current;

        Assert.False(heartbeatResponse.StreamToken.IsEmpty);
        Assert.NotEqual(firstToken, heartbeatResponse.StreamToken);
        Assert.Empty(heartbeatResponse.WriteResults);
        Assert.Null(heartbeatResponse.CommitTime);

        await call.RequestStream.CompleteAsync();
    }

    [Fact]
    public async Task Write_StreamResumption_ThrowsUnimplemented()
    {
        var builder = new DocumentBuilder();
        using var call = Client.Write(cancellationToken: TestContext.Current.CancellationToken);

        // Try to resume a stream by sending a stream_id on first message
        await call.RequestStream.WriteAsync(
            new WriteRequest { Database = builder.Database, StreamId = "some-old-stream-id" },
            TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            while (await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)) { }
        });
        Assert.Equal(StatusCode.Unimplemented, ex.StatusCode);
    }

    [Fact]
    public async Task Write_WritesInFirstMessage_ThrowsInvalidArgument()
    {
        var docBuilder = new DocumentBuilder().WithCollection("ws-tests").WithId("ws-invalid-first").WithField("v", "x");
        using var call = Client.Write(cancellationToken: TestContext.Current.CancellationToken);

        var badFirst = new WriteRequest { Database = docBuilder.Database };
        badFirst.Writes.Add(docBuilder.BuildUpsertWrite());
        await call.RequestStream.WriteAsync(badFirst, TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            while (await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken)) { }
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task Write_StreamTokenChangesEveryResponse()
    {
        var builder = new DocumentBuilder();
        using var call = Client.Write(cancellationToken: TestContext.Current.CancellationToken);

        await call.RequestStream.WriteAsync(builder.BuildWriteHandshake(), TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        var token1 = call.ResponseStream.Current.StreamToken;

        // Second heartbeat
        await call.RequestStream.WriteAsync(new WriteRequest(), TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        var token2 = call.ResponseStream.Current.StreamToken;

        // Third heartbeat
        await call.RequestStream.WriteAsync(new WriteRequest(), TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        var token3 = call.ResponseStream.Current.StreamToken;

        await call.RequestStream.CompleteAsync();

        // Each token should be unique (timestamp-based monotonic values)
        Assert.NotEqual(token1, token2);
        Assert.NotEqual(token2, token3);
    }
}
