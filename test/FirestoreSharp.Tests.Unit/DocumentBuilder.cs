using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;

using Value = Google.Cloud.Firestore.V1.Value;

namespace FirestoreSharp.Tests.Unit;

public sealed class DocumentBuilder
{
    private const string DefaultParent = "projects/test-project/databases/(default)/documents";

    private string _parent = DefaultParent;
    private string _collectionId = "testCollection";
    private string _documentId = Guid.NewGuid().ToString();
    private readonly Dictionary<string, Value> _fields = new();

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
        _fields[name] = new Value { StringValue = value };
        return this;
    }

    public DocumentBuilder WithField(string name, long value)
    {
        _fields[name] = new Value { IntegerValue = value };
        return this;
    }

    public DocumentBuilder WithField(string name, double value)
    {
        _fields[name] = new Value { DoubleValue = value };
        return this;
    }

    public DocumentBuilder WithField(string name, bool value)
    {
        _fields[name] = new Value { BooleanValue = value };
        return this;
    }

    public DocumentBuilder WithNullField(string name)
    {
        _fields[name] = new Value { NullValue = NullValue.NullValue };
        return this;
    }

    public string Parent => _parent;
    public string CollectionId => _collectionId;
    public string DocumentId => _documentId;
    public string ExpectedName => $"{_parent}/{_collectionId}/{_documentId}";

    public Document Build()
    {
        var doc = new Document();
        foreach (var (key, value) in _fields)
        {
            doc.Fields[key] = value;
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
}
