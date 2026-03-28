using System.Reflection;
using System.Security.Cryptography;
using FirestoreSharp.Core;
using Google.Cloud.Firestore.V1;
using Grpc.Core;

namespace FirestoreSharp.Server.UI;

internal static class UiEndpoints
{
    private const string DefaultProject = "local";
    private const string DefaultDatabase = "(default)";

    private static readonly char[] AutoIdChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

    public static WebApplication MapFirestoreUi(this WebApplication app)
    {
        app.MapGet("/ui", () => Results.Redirect("/ui/index.html"));
        app.MapGet("/ui/{*path}", ServeStaticFile);

        var api = app.MapGroup("/api/ui");
        api.MapGet("/config", GetConfig);
        api.MapGet("/collections", ListCollections);
        api.MapGet("/documents", ListDocuments);
        api.MapGet("/document", GetDocument);
        api.MapPost("/document", CreateDocument);
        api.MapPut("/document", UpdateDocument);
        api.MapDelete("/document", DeleteDocument);

        return app;
    }

    private static IResult ServeStaticFile(string? path)
    {
        var resourcePath = (path ?? "index.html").Replace('/', '.');
        var resourceName = $"FirestoreSharp.Server.UI.wwwroot.{resourcePath}";
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
            return Results.NotFound();

        var contentType = resourcePath switch
        {
            var p when p.EndsWith(".js", StringComparison.OrdinalIgnoreCase) => "application/javascript",
            var p when p.EndsWith(".css", StringComparison.OrdinalIgnoreCase) => "text/css",
            _ => "text/html; charset=utf-8"
        };

        return Results.Stream(stream, contentType);
    }

    private static IResult GetConfig() =>
        Results.Ok(new ConfigResponse(DefaultProject, DefaultDatabase));

    private static async Task<IResult> ListCollections(
        string parent,
        string? pageToken,
        IDocumentService documentService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await documentService.ListCollectionIdsAsync(parent, 100, pageToken, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new CollectionListResponse(result.CollectionIds, result.NextPageToken));
        }
        catch (RpcException ex)
        {
            return MapGrpcError(ex);
        }
    }

    private static async Task<IResult> ListDocuments(
        string parent,
        string collectionId,
        string? pageToken,
        IDocumentService documentService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await documentService.ListAsync(parent, collectionId, 50, pageToken, cancellationToken).ConfigureAwait(false);
            var summaries = result.Documents.Select(ToDocumentSummary).ToList();
            return Results.Ok(new DocumentListResponse(summaries, result.NextPageToken));
        }
        catch (RpcException ex)
        {
            return MapGrpcError(ex);
        }
    }

    private static async Task<IResult> GetDocument(
        string resourceName,
        IDocumentService documentService,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = DocumentPath.Parse(resourceName);
            var doc = await documentService.GetAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);
            return Results.Ok(ToDocumentResponse(doc));
        }
        catch (RpcException ex)
        {
            return MapGrpcError(ex);
        }
    }

    private static async Task<IResult> CreateDocument(
        string parent,
        string collectionId,
        CreateDocumentRequest body,
        IDocumentService documentService,
        CancellationToken cancellationToken)
    {
        try
        {
            var documentId = string.IsNullOrWhiteSpace(body.DocumentId)
                ? GenerateAutoId()
                : body.DocumentId;

            var path = DocumentPath.FromCreateRequest(parent, collectionId, documentId);
            var doc = new Document();
            if (body.Fields is not null)
            {
                foreach (var (key, uiVal) in body.Fields)
                    doc.Fields[key] = ValueConverter.FromUiValue(uiVal);
            }

            var created = await documentService.CreateAsync(path, doc, cancellationToken).ConfigureAwait(false);
            return Results.Ok(ToDocumentResponse(created));
        }
        catch (RpcException ex)
        {
            return MapGrpcError(ex);
        }
    }

    private static async Task<IResult> UpdateDocument(
        string resourceName,
        UpdateDocumentRequest body,
        IDocumentService documentService,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = DocumentPath.Parse(resourceName);
            var doc = new Document { Name = resourceName };
            if (body.Fields is not null)
            {
                foreach (var (key, uiVal) in body.Fields)
                    doc.Fields[key] = ValueConverter.FromUiValue(uiVal);
            }

            var updated = await documentService.UpdateAsync(path, doc, body.UpdateMask, cancellationToken).ConfigureAwait(false);
            return Results.Ok(ToDocumentResponse(updated));
        }
        catch (RpcException ex)
        {
            return MapGrpcError(ex);
        }
    }

    private static async Task<IResult> DeleteDocument(
        string resourceName,
        IDocumentService documentService,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = DocumentPath.Parse(resourceName);
            await documentService.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (RpcException ex)
        {
            return MapGrpcError(ex);
        }
    }

    private static DocumentSummary ToDocumentSummary(Document doc)
    {
        var docId = doc.Name[(doc.Name.LastIndexOf('/') + 1)..];
        var fields = doc.Fields.ToDictionary(kv => kv.Key, kv => ValueConverter.ToUiValue(kv.Value));
        return new DocumentSummary(
            doc.Name,
            docId,
            fields,
            doc.CreateTime?.ToDateTimeOffset().ToString("O"),
            doc.UpdateTime?.ToDateTimeOffset().ToString("O"));
    }

    private static DocumentResponse ToDocumentResponse(Document doc)
    {
        var docId = doc.Name[(doc.Name.LastIndexOf('/') + 1)..];
        var fields = doc.Fields.ToDictionary(kv => kv.Key, kv => ValueConverter.ToUiValue(kv.Value));
        return new DocumentResponse(
            doc.Name,
            docId,
            fields,
            doc.CreateTime?.ToDateTimeOffset().ToString("O"),
            doc.UpdateTime?.ToDateTimeOffset().ToString("O"));
    }

    private static IResult MapGrpcError(RpcException ex) =>
        ex.StatusCode switch
        {
            StatusCode.NotFound => Results.Problem(ex.Status.Detail, statusCode: 404),
            StatusCode.AlreadyExists => Results.Problem(ex.Status.Detail, statusCode: 409),
            StatusCode.InvalidArgument => Results.Problem(ex.Status.Detail, statusCode: 400),
            StatusCode.PermissionDenied => Results.Problem(ex.Status.Detail, statusCode: 403),
            _ => Results.Problem(ex.Status.Detail, statusCode: 500)
        };

    private static string GenerateAutoId()
    {
        var chars = new char[20];
        var bytes = RandomNumberGenerator.GetBytes(20);
        for (var i = 0; i < 20; i++)
            chars[i] = AutoIdChars[bytes[i] % AutoIdChars.Length];
        return new string(chars);
    }
}
