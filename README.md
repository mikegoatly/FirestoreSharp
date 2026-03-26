# FirestoreSharp

This project is an open source .NET emulator for Google Cloud Firestore. It is designed to provide a local development environment 
for testing and development purposes without the need to connect to the actual Firestore service.

The motivation behind this project is to avoid the memory challenges faced when using the official Firestore emulator, which stores
data in memory. By using a file-based storage approach, FirestoreSharp allows for larger datasets to be handled without running into 
memory limitations.

# Progress and Roadmap

## RPC Methods

### Document CRUD

| RPC | Request | Response | Streaming | Status |
|-----|---------|----------|-----------|-------------|
| `GetDocument` | `GetDocumentRequest` | `Document` | Unary | Not implemented |
| `ListDocuments` | `ListDocumentsRequest` | `ListDocumentsResponse` | Unary | Not implemented |
| `CreateDocument` | `CreateDocumentRequest` | `Document` | Unary | Not implemented |
| `UpdateDocument` | `UpdateDocumentRequest` | `Document` | Unary | Not implemented |
| `DeleteDocument` | `DeleteDocumentRequest` | `Empty` | Unary | Not implemented |
| `BatchGetDocuments` | `BatchGetDocumentsRequest` | `BatchGetDocumentsResponse` | **Server streaming** | Not implemented |

### Queries

| RPC | Request | Response | Streaming | Status |
|-----|---------|----------|-----------|-------------|
| `RunQuery` | `RunQueryRequest` | `RunQueryResponse` | **Server streaming** | Not implemented |
| `RunAggregationQuery` | `RunAggregationQueryRequest` | `RunAggregationQueryResponse` | **Server streaming** | Not implemented |
| `PartitionQuery` | `PartitionQueryRequest` | `PartitionQueryResponse` | Unary | Not implemented |
| `ExecutePipeline` | `ExecutePipelineRequest` | `ExecutePipelineResponse` | **Server streaming** | Not implemented |

### Transactions

| RPC | Request | Response | Streaming | Status |
|-----|---------|----------|-----------|-------------|
| `BeginTransaction` | `BeginTransactionRequest` | `BeginTransactionResponse` | Unary | Not implemented |
| `Commit` | `CommitRequest` | `CommitResponse` | Unary | Not implemented |
| `Rollback` | `RollbackRequest` | `Empty` | Unary | Not implemented |

### Batch/Streaming Writes

| RPC | Request | Response | Streaming | Status |
|-----|---------|----------|-----------|-------------|
| `BatchWrite` | `BatchWriteRequest` | `BatchWriteResponse` | Unary | Not implemented |
| `Write` | `WriteRequest` | `WriteResponse` | **Bidirectional streaming** | Not implemented |

### Real-time Listeners

| RPC | Request | Response | Streaming | Status |
|-----|---------|----------|-----------|-------------|
| `Listen` | `ListenRequest` | `ListenResponse` | **Bidirectional streaming** | Not implemented |

### Collection Management

| RPC | Request | Response | Streaming | Status |
|-----|---------|----------|-----------|-------------|
| `ListCollectionIds` | `ListCollectionIdsRequest` | `ListCollectionIdsResponse` | Unary | Not implemented |