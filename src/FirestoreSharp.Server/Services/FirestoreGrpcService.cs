using FirestoreSharp.Core;
using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace FirestoreSharp.Server.Services;

public sealed class FirestoreGrpcService(DocumentService documentService) : Firestore.FirestoreBase
{
    public override async Task<Document> CreateDocument(CreateDocumentRequest request, ServerCallContext context)
    {
        var path = FirestorePath.FromCreateRequest(request.Parent, request.CollectionId, request.DocumentId);
        return await documentService.CreateAsync(path, request.Document, context.CancellationToken);
    }

    public override async Task<Document> GetDocument(GetDocumentRequest request, ServerCallContext context)
    {
        var path = FirestorePath.Parse(request.Name);
        return await documentService.GetAsync(path, context.CancellationToken);
    }

    public override async Task<Document> UpdateDocument(UpdateDocumentRequest request, ServerCallContext context)
    {
        var path = FirestorePath.Parse(request.Document.Name);
        var maskPaths = request.UpdateMask?.FieldPaths;
        IReadOnlyList<string>? updateMask = maskPaths is { Count: > 0 } ? [.. maskPaths] : null;
        return await documentService.UpdateAsync(path, request.Document, updateMask, context.CancellationToken);
    }

    public override async Task<Empty> DeleteDocument(DeleteDocumentRequest request, ServerCallContext context)
    {
        var path = FirestorePath.Parse(request.Name);
        await documentService.DeleteAsync(path, context.CancellationToken);
        return new Empty();
    }
}
