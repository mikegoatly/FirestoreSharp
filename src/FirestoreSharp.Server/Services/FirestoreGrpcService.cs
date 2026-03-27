using FirestoreSharp.Core;
using FirestoreSharp.Core.Listeners;
using FirestoreSharp.Core.Transactions;

using Google.Cloud.Firestore.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Microsoft.AspNetCore.Connections;

namespace FirestoreSharp.Server.Services;

#pragma warning disable CA1515 // Consider making public types internal
public sealed class FirestoreGrpcService(IDocumentService documentService, ITransactionManager transactionManager, IListenerService listenerService) : Firestore.FirestoreBase
#pragma warning restore CA1515 // Consider making public types internal
{
    public override async Task<Document> CreateDocument(CreateDocumentRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var path = DocumentPath.FromCreateRequest(request.Parent, request.CollectionId, request.DocumentId);
        return await documentService.CreateAsync(path, request.Document, context.CancellationToken).ConfigureAwait(false);
    }

    public override async Task<Document> GetDocument(GetDocumentRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var path = DocumentPath.Parse(request.Name);
        var doc = await documentService.GetAsync(path, context.CancellationToken).ConfigureAwait(false);

        if (request.ConsistencySelectorCase == GetDocumentRequest.ConsistencySelectorOneofCase.Transaction
            && !request.Transaction.IsEmpty)
        {
            transactionManager.RecordRead(request.Transaction, doc.Name, doc.UpdateTime);
        }

        return doc;
    }

    public override async Task<Document> UpdateDocument(UpdateDocumentRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var path = DocumentPath.Parse(request.Document.Name);
        var maskPaths = request.UpdateMask?.FieldPaths;
        IReadOnlyList<string>? updateMask = maskPaths is { Count: > 0 } ? [.. maskPaths] : null;
        return await documentService.UpdateAsync(path, request.Document, updateMask, context.CancellationToken).ConfigureAwait(false);
    }

    public override async Task<Empty> DeleteDocument(DeleteDocumentRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var path = DocumentPath.Parse(request.Name);
        await documentService.DeleteAsync(path, context.CancellationToken).ConfigureAwait(false);
        return new Empty();
    }

    public override async Task BatchGetDocuments(BatchGetDocumentsRequest request, IServerStreamWriter<BatchGetDocumentsResponse> responseStream, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        var isTransactional = request.ConsistencySelectorCase == BatchGetDocumentsRequest.ConsistencySelectorOneofCase.Transaction
                              && !request.Transaction.IsEmpty;

        await foreach (var result in documentService.BatchGetAsync([.. request.Documents], context.CancellationToken).ConfigureAwait(false))
        {
            var response = result switch
            {
                BatchGetFoundResult found => new BatchGetDocumentsResponse { Found = found.Document, ReadTime = found.ReadTime },
                BatchGetMissingResult missing => new BatchGetDocumentsResponse { Missing = missing.ResourceName, ReadTime = missing.ReadTime },
                _ => throw new InvalidOperationException($"Unexpected BatchGetResult type: {result.GetType()}")
            };

            if (isTransactional)
            {
                var (docName, updateTime) = result switch
                {
                    BatchGetFoundResult found => (found.Document.Name, found.Document.UpdateTime),
                    BatchGetMissingResult missing => (missing.ResourceName, (Google.Protobuf.WellKnownTypes.Timestamp?)null),
                    _ => throw new InvalidOperationException($"Unexpected BatchGetResult type: {result.GetType()}")
                };
                transactionManager.RecordRead(request.Transaction, docName, updateTime);
            }

            await responseStream.WriteAsync(response, context.CancellationToken).ConfigureAwait(false);
        }
    }

    public override async Task<ListCollectionIdsResponse> ListCollectionIds(ListCollectionIdsRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var result = await documentService.ListCollectionIdsAsync(
            request.Parent,
            request.PageSize,
            string.IsNullOrEmpty(request.PageToken) ? null : request.PageToken,
            context.CancellationToken).ConfigureAwait(false);

        var response = new ListCollectionIdsResponse();
        response.CollectionIds.AddRange(result.CollectionIds);
        if (result.NextPageToken is not null)
        {
            response.NextPageToken = result.NextPageToken;
        }

        return response;
    }

    public override async Task<ListDocumentsResponse> ListDocuments(ListDocumentsRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var result = await documentService.ListAsync(
            request.Parent,
            request.CollectionId,
            request.PageSize,
            request.PageToken,
            context.CancellationToken).ConfigureAwait(false);

        var response = new ListDocumentsResponse();
        response.Documents.AddRange(result.Documents);
        if (result.NextPageToken is not null)
        {
            response.NextPageToken = result.NextPageToken;
        }

        return response;
    }

    public override async Task RunQuery(RunQueryRequest request, IServerStreamWriter<RunQueryResponse> responseStream, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        if (request.QueryTypeCase != RunQueryRequest.QueryTypeOneofCase.StructuredQuery)
        {
            throw new RpcException(new Status(StatusCode.Unimplemented, "Only structured queries are supported."));
        }

        var readTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        var isTransactional = request.ConsistencySelectorCase == RunQueryRequest.ConsistencySelectorOneofCase.Transaction
                              && !request.Transaction.IsEmpty;

        var documents = await documentService.RunQueryAsync(
            request.Parent,
            request.StructuredQuery,
            context.CancellationToken).ConfigureAwait(false);

        foreach (var document in documents)
        {
            if (isTransactional)
            {
                transactionManager.RecordRead(request.Transaction, document.Name, document.UpdateTime);
            }

            await responseStream.WriteAsync(
                new RunQueryResponse { Document = document, ReadTime = readTime },
                context.CancellationToken).ConfigureAwait(false);
        }

        // Always send a final response with read_time and done=true (even when no docs matched)
        await responseStream.WriteAsync(
            new RunQueryResponse { ReadTime = readTime, Done = true },
            context.CancellationToken).ConfigureAwait(false);
    }

    public override async Task RunAggregationQuery(RunAggregationQueryRequest request, IServerStreamWriter<RunAggregationQueryResponse> responseStream, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        if (request.QueryTypeCase != RunAggregationQueryRequest.QueryTypeOneofCase.StructuredAggregationQuery)
        {
            throw new RpcException(new Status(StatusCode.Unimplemented, "Only structured aggregation queries are supported."));
        }

        var readTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        // Resolve the effective transaction ID (existing or newly started).
        Google.Protobuf.ByteString? effectiveTransaction = null;
        if (request.ConsistencySelectorCase == RunAggregationQueryRequest.ConsistencySelectorOneofCase.NewTransaction)
        {
            effectiveTransaction = transactionManager.BeginTransaction(request.NewTransaction);
            // First response carries the new transaction ID and no result.
            await responseStream.WriteAsync(
                new RunAggregationQueryResponse { Transaction = effectiveTransaction, ReadTime = readTime },
                context.CancellationToken).ConfigureAwait(false);
        }
        else if (request.ConsistencySelectorCase == RunAggregationQueryRequest.ConsistencySelectorOneofCase.Transaction
                 && !request.Transaction.IsEmpty)
        {
            effectiveTransaction = request.Transaction;
        }

        var queryResult = await documentService.RunAggregationQueryAsync(
            request.Parent,
            request.StructuredAggregationQuery,
            context.CancellationToken).ConfigureAwait(false);

        // Record each scanned document against the transaction's read-set for conflict detection.
        if (effectiveTransaction is not null)
        {
            foreach (var doc in queryResult.SourceDocuments)
            {
                transactionManager.RecordRead(effectiveTransaction, doc.Name, doc.UpdateTime);
            }
        }

        await responseStream.WriteAsync(
            new RunAggregationQueryResponse { Result = queryResult.AggregationResult, ReadTime = readTime },
            context.CancellationToken).ConfigureAwait(false);
    }

    public override async Task<CommitResponse> Commit(CommitRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyDictionary<string, Timestamp?>? readSet = null;

        if (!request.Transaction.IsEmpty)
        {
            if (request.Writes.Count > 0)
            {
                transactionManager.ValidateCanWrite(request.Transaction);
            }

            readSet = transactionManager.GetReadSet(request.Transaction);
            transactionManager.Complete(request.Transaction);
        }

        var results = await documentService.CommitAsync(
            [.. request.Writes],
            readSet,
            context.CancellationToken).ConfigureAwait(false);

        var commitTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        var response = new CommitResponse { CommitTime = commitTime };
        response.WriteResults.Add(results);

        return response;
    }

    public override Task<BeginTransactionResponse> BeginTransaction(BeginTransactionRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var transactionId = transactionManager.BeginTransaction(request.Options);

        return Task.FromResult(new BeginTransactionResponse { Transaction = transactionId });
    }

    public override Task<Empty> Rollback(RollbackRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        // Validate the transaction exists and is not expired, then remove it
        transactionManager.ValidateAndComplete(request.Transaction);

        return Task.FromResult(new Empty());
    }

    public override async Task<BatchWriteResponse> BatchWrite(BatchWriteRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var response = new BatchWriteResponse();

        foreach (var write in request.Writes)
        {
            try
            {
                var result = await documentService.ExecuteWriteAsync(write, context.CancellationToken).ConfigureAwait(false);
                response.WriteResults.Add(result);
                response.Status.Add(new Google.Rpc.Status { Code = (int)StatusCode.OK });
            }
            catch (RpcException ex)
            {
                response.WriteResults.Add(new WriteResult());
                response.Status.Add(new Google.Rpc.Status { Code = (int)ex.StatusCode, Message = ex.Status.Detail });
            }
        }

        return response;
    }

    public override async Task Write(
        IAsyncStreamReader<WriteRequest> requestStream,
        IServerStreamWriter<WriteResponse> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(requestStream);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        // First message must arrive to open the stream
        if (!await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var firstRequest = requestStream.Current;

        // Stream resumption is not supported
        if (!string.IsNullOrEmpty(firstRequest.StreamId))
        {
            throw new RpcException(new Status(StatusCode.Unimplemented,
                "Write stream resumption is not supported."));
        }

        // First message must have no writes (protocol handshake)
        if (firstRequest.Writes.Count > 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "The first Write request must not contain writes."));
        }

        var streamId = Guid.NewGuid().ToString("N");

        // Send the handshake response with stream_id and an initial stream_token
        await responseStream.WriteAsync(new WriteResponse
        {
            StreamId = streamId,
            StreamToken = ByteString.New(),
        }, context.CancellationToken).ConfigureAwait(false);

        // Process subsequent requests until the client closes the stream
        while (await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
        {
            var request = requestStream.Current;

            // Empty writes = heartbeat — respond with a refreshed token
            if (request.Writes.Count == 0)
            {
                await responseStream.WriteAsync(new WriteResponse
                {
                    StreamToken = ByteString.New(),
                }, context.CancellationToken).ConfigureAwait(false);
                continue;
            }

            var results = await documentService.CommitAsync(
                [.. request.Writes],
                null,
                context.CancellationToken).ConfigureAwait(false);

            var commitTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
            var response = new WriteResponse
            {
                StreamToken = ByteString.New(),
                CommitTime = commitTime,
            };
            response.WriteResults.Add(results);

            await responseStream.WriteAsync(response, context.CancellationToken).ConfigureAwait(false);
        }
    }

    public override async Task Listen(
        IAsyncStreamReader<ListenRequest> requestStream,
        IServerStreamWriter<ListenResponse> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(requestStream);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        var connection = listenerService.CreateConnection();
        Task? writerTask = null;
        try
        {
            // Background task: read from the connection's channel and write to the gRPC response stream.
            writerTask = Task.Run(async () =>
            {
                await foreach (var response in connection.Responses.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
                {
                    await responseStream.WriteAsync(response, context.CancellationToken).ConfigureAwait(false);
                }
            }, context.CancellationToken);

            // Main loop: read client ListenRequests and dispatch them.
            while (await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
            {
                var request = requestStream.Current;

                switch (request.TargetChangeCase)
                {
                    case ListenRequest.TargetChangeOneofCase.AddTarget:
                        await connection.AddTargetAsync(request.AddTarget, context.CancellationToken).ConfigureAwait(false);
                        break;

                    case ListenRequest.TargetChangeOneofCase.RemoveTarget:
                        connection.RemoveTarget(request.RemoveTarget);
                        break;
                }
            }
        }
        finally
        {
            // Dispose the connection first — this completes the channel, which lets the writer task
            // drain and exit naturally rather than block forever.
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (ConnectionAbortedException) { }

            // Await the writer task to observe any exceptions. Swallow cancellation and gRPC
            // connectivity failures (connection reset, client disconnect) since those aren't bugs —
            // any genuine unexpected exception will still propagate from here.
            if (writerTask is not null)
            {
                try
                {
                    await writerTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (RpcException ex) when (ex.StatusCode is StatusCode.Cancelled or StatusCode.Unavailable) { }
            }
        }
    }
}

