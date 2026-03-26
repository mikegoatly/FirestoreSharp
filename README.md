# FirestoreSharp

> This is very early stage work-in-progress. The API and design are not finalized and are subject to change.

This project is an open source .NET emulator for Google Cloud Firestore. It is designed to provide a local development environment 
for testing and development purposes without the need to connect to the actual Firestore service.

The motivation behind this project is to avoid the memory challenges faced when using the official Firestore emulator. By having the 
option to use a file-based storage approach, FirestoreSharp allows for larger datasets to be handled without running into 
memory limitations.

# Progress and Roadmap

## RPC Methods

### Document CRUD

| RPC | Request | Response | Streaming | Status |
|-----|---------|----------|-----------|-------------|
| `GetDocument` | `GetDocumentRequest` | `Document` | Unary | ✅ Done |
| `ListDocuments` | `ListDocumentsRequest` | `ListDocumentsResponse` | Unary | ✅ Done |
| `CreateDocument` | `CreateDocumentRequest` | `Document` | Unary | ✅ Done |
| `UpdateDocument` | `UpdateDocumentRequest` | `Document` | Unary | ✅ Done |
| `DeleteDocument` | `DeleteDocumentRequest` | `Empty` | Unary | ✅ Done |
| `BatchGetDocuments` | `BatchGetDocumentsRequest` | `BatchGetDocumentsResponse` | **Server streaming** | ✅ Done |

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

## Storage Layer

- File-based storage implementation
- In-memory storage implementation

## Releases

The plan is to make this available as a self contained docker container as well. Other options, including
a self hosted library, dotnet tool, etc. will also be considered.