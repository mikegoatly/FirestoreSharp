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
| `RunQuery` | `RunQueryRequest` | `RunQueryResponse` | **Server streaming** | ✅ Done (partial — see below) |
| `RunAggregationQuery` | `RunAggregationQueryRequest` | `RunAggregationQueryResponse` | **Server streaming** | Not implemented |
| `PartitionQuery` | `PartitionQueryRequest` | `PartitionQueryResponse` | Unary | Not implemented |
| `ExecutePipeline` | `ExecutePipelineRequest` | `ExecutePipelineResponse` | **Server streaming** | Not implemented |

#### RunQuery — Supported StructuredQuery Features

| Feature | Status | Notes |
|---------|--------|-------|
| `from` — direct collection | ✅ | Queries documents in a single named collection |
| `from` — collection group (`all_descendants: true`) | ✅ | Queries across all subcollections with the same ID |
| `where` — `EQUAL` / `NOT_EQUAL` | ✅ | |
| `where` — `LESS_THAN` / `LESS_THAN_OR_EQUAL` | ✅ | |
| `where` — `GREATER_THAN` / `GREATER_THAN_OR_EQUAL` | ✅ | |
| `where` — `IN` / `NOT_IN` | ✅ | |
| `where` — `ARRAY_CONTAINS` / `ARRAY_CONTAINS_ANY` | ✅ | |
| `where` — `IS_NULL` / `IS_NOT_NULL` (unary) | ✅ | |
| `where` — `IS_NAN` / `IS_NOT_NAN` (unary) | ✅ | |
| `where` — composite `AND` / `OR` | ✅ | Arbitrarily nested |
| `order_by` — explicit field ordering (ASC / DESC) | ✅ | |
| `order_by` — implicit `__name__` appending | ✅ | Firestore tiebreak semantics |
| `select` — field projection | ✅ | Returns only requested fields |
| `offset` | ✅ | |
| `limit` | ✅ | |
| `__name__` pseudo-field in filters / ordering | ✅ | Resolved to `Document.Name` |
| Firestore value ordering (cross-type) | ✅ | null < bool < number < timestamp < string < bytes < reference < geo_point < array < map |
| NaN ordering (before all numbers) | ✅ | |
| `start_at` / `end_at` cursors | ❌ Not implemented | |
| `find_nearest` (vector search) | ❌ Not implemented | |
| `consistency_selector` (transactions / read_time) | ✅ | Transactions supported; `read_time` not yet supported |
| `explain_options` | ❌ Not implemented | |

### Transactions

| RPC | Request | Response | Streaming | Status |
|-----|---------|----------|-----------|-------------|
| `BeginTransaction` | `BeginTransactionRequest` | `BeginTransactionResponse` | Unary | ✅ Done |
| `Commit` | `CommitRequest` | `CommitResponse` | Unary | ✅ Done |
| `Rollback` | `RollbackRequest` | `Empty` | Unary | ✅ Done |

#### Transactions — Supported Features

| Feature | Status | Notes |
|---------|--------|-------|
| `BeginTransaction` (ReadWrite / ReadOnly modes) | ✅ | |
| `Commit` (transactional and non-transactional) | ✅ | Atomic all-or-nothing (prepare-then-apply) |
| `Rollback` | ✅ | |
| Optimistic concurrency (read-set conflict → `ABORTED`) | ✅ | Conflicting transactions automatically aborted |
| `retry_transaction` support | ✅ | Accepted in `TransactionOptions.ReadWrite` |
| 60-second idle expiry | ✅ | |
| Transaction-scoped reads (Get, BatchGet, RunQuery) | ✅ | Read-set tracked for conflict detection |
| Read-only transaction commit (no writes) | ✅ | |
| Pessimistic concurrency (document locking) | ❌ Not implemented | Optimistic only |
| True snapshot isolation (MVCC) | ❌ Not implemented | Reads return current state; conflicts caught at commit |
| 500-document transaction limit | ✅ | Enforced — rejects with `INVALID_ARGUMENT` |

### Batch/Streaming Writes

| RPC | Request | Response | Streaming | Status |
|-----|---------|----------|-----------|-------------|
| `BatchWrite` | `BatchWriteRequest` | `BatchWriteResponse` | Unary | ✅ Done |
| `Write` | `WriteRequest` | `WriteResponse` | **Bidirectional streaming** | ✅ Done (partial — see below) |

#### Write — Supported Features

| Feature | Status | Notes |
|---------|--------|-------|
| Stream handshake (open stream, receive `stream_id` + `stream_token`) | ✅ | |
| Write batch per request (atomic commit) | ✅ | Reuses transaction commit semantics |
| Heartbeat (empty writes → token refresh) | ✅ | |
| `stream_token` round-trip (unique token every response) | ✅ | |
| Stream resumption (`stream_id` on open) | ❌ Not implemented | Rejected with `UNIMPLEMENTED` |

### Real-time Listeners

| RPC | Request | Response | Streaming | Status |
|-----|---------|----------|-----------|-------------|
| `Listen` | `ListenRequest` | `ListenResponse` | **Bidirectional streaming** | ✅ Done (partial — see below) |

#### Listen — Supported Features

| Feature | Status | Notes |
|---------|--------|-------|
| Document targets (watch specific documents) | ✅ | Initial snapshot + live create/update/delete notifications |
| Query targets (watch a structured query) | ✅ | Initial snapshot + live notifications for matching documents |
| `TargetChange` lifecycle (ADD, REMOVE, CURRENT) | ✅ | Sent during target registration and removal |
| `DocumentChange` notifications | ✅ | Sent when a watched/matching document is created or updated |
| `DocumentDelete` notifications | ✅ | Sent when a watched/matching document is deleted |
| `DocumentRemove` notifications | ✅ | Sent when a document no longer matches a query target |
| Multiple targets per stream | ✅ | Add/remove targets dynamically |
| Target ID auto-assignment (`target_id = 0`) | ✅ | Server assigns a unique ID |
| Resume tokens (`resume_token` / `read_time`) | ❌ Not implemented | |
| `ExistenceFilter` / BloomFilter reconciliation | ❌ Not implemented | |
| `once` flag (single snapshot then remove) | ❌ Not implemented | |
| Limit-based removal tracking | ❌ Not implemented | Query limit overflow won't send `DocumentRemove` |

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