using FirestoreSharp.Core;
using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;

using Value = Google.Cloud.Firestore.V1.Value;

namespace FirestoreSharp.Tests.Unit.Builders;

internal sealed class DocumentBuilder
{
    private const string DefaultParent = "projects/test-project/databases/(default)/documents";

    private string _parent = DefaultParent;
    private string _collectionId = "testCollection";
    private string _documentId = Guid.NewGuid().ToString();
    private readonly List<(FieldPath Path, Value Value)> _fields = [];

    public DocumentBuilder WithParent(string parent)
    {
        _parent = parent;
        return this;
    }

    public DocumentBuilder WithCollection(string collectionId)
    {
        _collectionId = collectionId;
        return this;
    }

    public DocumentBuilder WithId(string documentId)
    {
        _documentId = documentId;
        return this;
    }

    public DocumentBuilder WithField(string name, string value)
    {
        _fields.Add((FieldPath.Parse(name), new Value { StringValue = value }));
        return this;
    }

    public DocumentBuilder WithField(string name, long value)
    {
        _fields.Add((FieldPath.Parse(name), new Value { IntegerValue = value }));
        return this;
    }

    public DocumentBuilder WithField(string name, double value)
    {
        _fields.Add((FieldPath.Parse(name), new Value { DoubleValue = value }));
        return this;
    }

    public DocumentBuilder WithField(string name, bool value)
    {
        _fields.Add((FieldPath.Parse(name), new Value { BooleanValue = value }));
        return this;
    }

    public DocumentBuilder WithNullField(string name)
    {
        _fields.Add((FieldPath.Parse(name), new Value { NullValue = NullValue.NullValue }));
        return this;
    }

    public string Parent => _parent;
    public string CollectionId => _collectionId;
    public string DocumentId => _documentId;
    public string ExpectedName => $"{_parent}/{_collectionId}/{_documentId}";

    public DocumentPath BuildPath()
    {
        return DocumentPath.FromCreateRequest(_parent, _collectionId, _documentId);
    }

    public Document Build()
    {
        var doc = new Document { Name = ExpectedName };
        foreach (var (path, value) in _fields)
        {
            DocumentNavigator.SetValue(doc, path, value);
        }
        return doc;
    }

    public CreateDocumentRequest BuildCreateRequest()
    {
        return new CreateDocumentRequest
        {
            Parent = _parent,
            CollectionId = _collectionId,
            DocumentId = _documentId,
            Document = Build()
        };
    }

    public GetDocumentRequest BuildGetRequest()
    {
        return new GetDocumentRequest
        {
            Name = ExpectedName
        };
    }

    public UpdateDocumentRequest BuildUpdateRequest(params string[] updateMaskFieldPaths)
    {
        var request = new UpdateDocumentRequest
        {
            Document = Build()
        };

        if (updateMaskFieldPaths.Length > 0)
        {
            request.UpdateMask = new DocumentMask();
            request.UpdateMask.FieldPaths.AddRange(updateMaskFieldPaths);
        }

        return request;
    }

    public DeleteDocumentRequest BuildDeleteRequest()
    {
        return new DeleteDocumentRequest
        {
            Name = ExpectedName
        };
    }

    public ListDocumentsRequest BuildListRequest(int pageSize = 0, string? pageToken = null)
    {
        var request = new ListDocumentsRequest
        {
            Parent = _parent,
            CollectionId = _collectionId,
            PageSize = pageSize
        };

        if (pageToken is not null)
        {
            request.PageToken = pageToken;
        }

        return request;
    }

    /// <summary>
    /// Builds a <see cref="RunQueryRequest"/> targeting the current collection from the current parent.
    /// Optionally add filters or ordering to the returned query.
    /// </summary>
    public RunQueryRequest BuildRunQueryRequest(Action<StructuredQuery>? configure = null)
    {
        var query = new StructuredQuery();
        query.From.Add(new StructuredQuery.Types.CollectionSelector
        {
            CollectionId = _collectionId,
            AllDescendants = false
        });

        configure?.Invoke(query);

        return new RunQueryRequest
        {
            Parent = _parent,
            StructuredQuery = query
        };
    }
}
