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

        var path = FirestorePath.FromCreateRequest(request.Parent, request.CollectionId, request.DocumentId);
        return await documentService.CreateAsync(path, request.Document, context.CancellationToken).ConfigureAwait(false);
    }

    public override async Task<Document> GetDocument(GetDocumentRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var path = FirestorePath.Parse(request.Name);
        return await documentService.GetAsync(path, context.CancellationToken).ConfigureAwait(false);
    }

    public override async Task<Document> UpdateDocument(UpdateDocumentRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var path = FirestorePath.Parse(request.Document.Name);
        var maskPaths = request.UpdateMask?.FieldPaths;
        IReadOnlyList<string>? updateMask = maskPaths is { Count: > 0 } ? [.. maskPaths] : null;
        return await documentService.UpdateAsync(path, request.Document, updateMask, context.CancellationToken).ConfigureAwait(false);
    }

    public override async Task<Empty> DeleteDocument(DeleteDocumentRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var path = FirestorePath.Parse(request.Name);
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
}
