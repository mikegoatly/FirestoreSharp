using FirestoreSharp.Core;
using Google.Cloud.Firestore.V1;
using Google.Protobuf;
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

    /// <summary>Database resource name derived from the parent, e.g. <c>projects/p/databases/(default)</c>.</summary>
    public string Database => _parent[.._parent.LastIndexOf("/documents", StringComparison.Ordinal)];

    public Write BuildUpsertWrite() => new() { Update = Build() };

    public Write BuildMaskedUpdateWrite(params string[] fieldPaths)
    {
        var write = new Write { Update = Build(), UpdateMask = new DocumentMask() };
        write.UpdateMask.FieldPaths.AddRange(fieldPaths);
        return write;
    }

    public Write BuildDeleteWrite() => new() { Delete = ExpectedName };

    public CommitRequest BuildCommitRequest(params Write[] writes)
    {
        var request = new CommitRequest { Database = Database };
        request.Writes.AddRange(writes);
        return request;
    }

    public BatchWriteRequest BuildBatchWriteRequest(params Write[] writes)
    {
        var request = new BatchWriteRequest { Database = Database };
        request.Writes.AddRange(writes);
        return request;
    }

    public BeginTransactionRequest BuildBeginTransactionRequest(TransactionOptions? options = null)
    {
        var request = new BeginTransactionRequest { Database = Database };
        if (options is not null)
        {
            request.Options = options;
        }

        return request;
    }

    public RollbackRequest BuildRollbackRequest(ByteString transactionId)
    {
        return new RollbackRequest { Database = Database, Transaction = transactionId };
    }

    public CommitRequest BuildTransactionalCommitRequest(ByteString transactionId, params Write[] writes)
    {
        var request = new CommitRequest { Database = Database, Transaction = transactionId };
        request.Writes.AddRange(writes);
        return request;
    }

    public GetDocumentRequest BuildTransactionalGetRequest(ByteString transactionId)
    {
        return new GetDocumentRequest { Name = ExpectedName, Transaction = transactionId };
    }

    public WriteRequest BuildWriteHandshake() => new() { Database = Database };

    public RunAggregationQueryRequest BuildAggregationQueryRequest(
        Action<StructuredAggregationQuery> configure,
        Google.Protobuf.ByteString? transaction = null,
        TransactionOptions? newTransaction = null)
    {
        var aggregationQuery = new StructuredAggregationQuery();

        // Default: query the current collection with no filters
        aggregationQuery.StructuredQuery = new StructuredQuery();
        aggregationQuery.StructuredQuery.From.Add(new StructuredQuery.Types.CollectionSelector
        {
            CollectionId = _collectionId,
            AllDescendants = false
        });

        configure(aggregationQuery);

        var request = new RunAggregationQueryRequest
        {
            Parent = _parent,
            StructuredAggregationQuery = aggregationQuery
        };

        if (transaction is not null)
        {
            request.Transaction = transaction;
        }
        else if (newTransaction is not null)
        {
            request.NewTransaction = newTransaction;
        }

        return request;
    }

    public PartitionQueryRequest BuildPartitionQueryRequest(long partitionCount, int pageSize = 0, string? pageToken = null)
    {
        var query = new StructuredQuery();
        query.From.Add(new StructuredQuery.Types.CollectionSelector
        {
            CollectionId = _collectionId,
            AllDescendants = true
        });
        query.OrderBy.Add(new StructuredQuery.Types.Order
        {
            Field = new StructuredQuery.Types.FieldReference { FieldPath = "__name__" },
            Direction = StructuredQuery.Types.Direction.Ascending
        });

        var request = new PartitionQueryRequest
        {
            Parent = _parent,
            StructuredQuery = query,
            PartitionCount = partitionCount,
            PageSize = pageSize
        };

        if (pageToken is not null)
        {
            request.PageToken = pageToken;
        }

        return request;
    }

    public ListCollectionIdsRequest BuildListCollectionIdsRequest(string? parent = null, int pageSize = 0, string? pageToken = null)
    {
        var request = new ListCollectionIdsRequest
        {
            Parent = parent ?? _parent,
            PageSize = pageSize
        };

        if (pageToken is not null)
        {
            request.PageToken = pageToken;
        }

        return request;
    }
}

