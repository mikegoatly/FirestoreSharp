using FirestoreSharp.Core;
using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace FirestoreSharp.Server.Services;

#pragma warning disable CA1515 // Consider making public types internal
public sealed class FirestoreGrpcService(IDocumentService documentService) : Firestore.FirestoreBase
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
        return await documentService.GetAsync(path, context.CancellationToken).ConfigureAwait(false);
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

        await foreach (var result in documentService.BatchGetAsync([.. request.Documents], context.CancellationToken).ConfigureAwait(false))
        {
            var response = result switch
            {
                BatchGetFoundResult found => new BatchGetDocumentsResponse { Found = found.Document, ReadTime = found.ReadTime },
                BatchGetMissingResult missing => new BatchGetDocumentsResponse { Missing = missing.ResourceName, ReadTime = missing.ReadTime },
                _ => throw new InvalidOperationException($"Unexpected BatchGetResult type: {result.GetType()}")
            };

            await responseStream.WriteAsync(response, context.CancellationToken).ConfigureAwait(false);
        }
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

        var documents = await documentService.RunQueryAsync(
            request.Parent,
            request.StructuredQuery,
            context.CancellationToken).ConfigureAwait(false);

        foreach (var document in documents)
        {
            await responseStream.WriteAsync(
                new RunQueryResponse { Document = document, ReadTime = readTime },
                context.CancellationToken).ConfigureAwait(false);
        }

        // Always send a final response with read_time and done=true (even when no docs matched)
        await responseStream.WriteAsync(
            new RunQueryResponse { ReadTime = readTime, Done = true },
            context.CancellationToken).ConfigureAwait(false);
    }

    public override async Task<CommitResponse> Commit(CommitRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        // TODO: Writes should be applied atomically (all-or-nothing). Implement a prepare-then-apply
        // pattern: validate all preconditions and build the full mutation set first (no store writes),
        // then apply all mutations. This same prepare/apply split will underpin BeginTransaction/Commit
        // with a transaction token, where the prepare phase happens at BeginTransaction time and the
        // apply phase happens here when the token is presented.

        var commitTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
        var response = new CommitResponse { CommitTime = commitTime };

        foreach (var write in request.Writes)
        {
            var result = await documentService.ExecuteWriteAsync(write, context.CancellationToken).ConfigureAwait(false);
            response.WriteResults.Add(result);
        }

        return response;
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
}

