using FirestoreSharp.Core;
using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace FirestoreSharp.Server.Services;

public sealed class FirestoreService(IDocumentStore store) : Firestore.FirestoreBase
{
    public override async Task<Document> CreateDocument(CreateDocumentRequest request, ServerCallContext context)
    {
        var path = FirestorePath.FromCreateRequest(request.Parent, request.CollectionId, request.DocumentId);
        var now = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        var document = request.Document.Clone();
        document.Name = path.ResourceName;
        document.CreateTime = now;
        document.UpdateTime = now;

        await store.CreateAsync(path, document, context.CancellationToken);

        return document;
    }

    public override async Task<Document> GetDocument(GetDocumentRequest request, ServerCallContext context)
    {
        var path = FirestorePath.Parse(request.Name);
        return await store.GetAsync(path, context.CancellationToken);
    }
}
