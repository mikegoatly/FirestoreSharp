# Firestore v1 gRPC API Surface

This document catalogs the full `google.firestore.v1.Firestore` gRPC service that our emulator must implement.

**Source proto files:** https://github.com/googleapis/googleapis/tree/master/google/firestore/v1

The key proto files are:
- `firestore.proto` — Main service definition and request/response messages
- `document.proto` — Document and Value types
- `query.proto` — StructuredQuery and filter types
- `common.proto` — Shared types (DocumentMask, Precondition, TransactionOptions)
- `write.proto` — Write and DocumentTransform types
- `aggregation_result.proto` — AggregationResult
- `pipeline.proto` — Pipeline and StructuredPipeline types
- `query_profile.proto` — ExplainMetrics, ExplainOptions
- `bloom_filter.proto` — BloomFilter, BitSequence (for Listen protocol)

---

## RPC Methods

### Document CRUD

| RPC | Request | Response | Streaming | Description |
|-----|---------|----------|-----------|-------------|
| `GetDocument` | `GetDocumentRequest` | `Document` | Unary | Gets a single document |
| `ListDocuments` | `ListDocumentsRequest` | `ListDocumentsResponse` | Unary | Lists documents with pagination, ordering, field masks |
| `CreateDocument` | `CreateDocumentRequest` | `Document` | Unary | Creates a new document (optionally with client-assigned ID) |
| `UpdateDocument` | `UpdateDocumentRequest` | `Document` | Unary | Updates or inserts a document (upsert) |
| `DeleteDocument` | `DeleteDocumentRequest` | `Empty` | Unary | Deletes a document |
| `BatchGetDocuments` | `BatchGetDocumentsRequest` | `BatchGetDocumentsResponse` | **Server streaming** | Gets multiple documents in one call |

### Queries

| RPC | Request | Response | Streaming | Description |
|-----|---------|----------|-----------|-------------|
| `RunQuery` | `RunQueryRequest` | `RunQueryResponse` | **Server streaming** | Runs a structured query |
| `RunAggregationQuery` | `RunAggregationQueryRequest` | `RunAggregationQueryResponse` | **Server streaming** | Runs an aggregation query (COUNT, SUM, AVG) |
| `PartitionQuery` | `PartitionQueryRequest` | `PartitionQueryResponse` | Unary | Partitions a query for parallel execution |
| `ExecutePipeline` | `ExecutePipelineRequest` | `ExecutePipelineResponse` | **Server streaming** | Executes a pipeline query (newer API) |

### Transactions

| RPC | Request | Response | Streaming | Description |
|-----|---------|----------|-----------|-------------|
| `BeginTransaction` | `BeginTransactionRequest` | `BeginTransactionResponse` | Unary | Starts a new transaction (read-only or read-write) |
| `Commit` | `CommitRequest` | `CommitResponse` | Unary | Commits a transaction with optional writes |
| `Rollback` | `RollbackRequest` | `Empty` | Unary | Rolls back a transaction |

### Batch/Streaming Writes

| RPC | Request | Response | Streaming | Description |
|-----|---------|----------|-----------|-------------|
| `BatchWrite` | `BatchWriteRequest` | `BatchWriteResponse` | Unary | Non-atomic batch writes (each write succeeds/fails independently) |
| `Write` | `WriteRequest` | `WriteResponse` | **Bidirectional streaming** | Streams batches of document updates/deletes. gRPC/WebChannel only. |

### Real-time Listeners

| RPC | Request | Response | Streaming | Description |
|-----|---------|----------|-----------|-------------|
| `Listen` | `ListenRequest` | `ListenResponse` | **Bidirectional streaming** | Listens to document/query changes in real-time. gRPC/WebChannel only. |

### Collection Management

| RPC | Request | Response | Streaming | Description |
|-----|---------|----------|-----------|-------------|
| `ListCollectionIds` | `ListCollectionIdsRequest` | `ListCollectionIdsResponse` | Unary | Lists all collection IDs underneath a document |

---

## Key Message Types

### Document

```
Document {
  string name                           // Resource name: projects/{project}/databases/{db}/documents/{path}
  map<string, Value> fields             // The document's fields
  Timestamp create_time                 // Output only
  Timestamp update_time                 // Output only
}
```

### Value (the core Firestore value type)

```
Value {
  oneof value_type {
    NullValue null_value
    bool boolean_value
    int64 integer_value
    double double_value
    Timestamp timestamp_value           // Microsecond precision
    string string_value                 // Max 1 MiB - 89 bytes
    bytes bytes_value                   // Max 1 MiB - 89 bytes
    string reference_value              // Document reference path
    LatLng geo_point_value              // Geographic point
    ArrayValue array_value              // Cannot directly nest arrays
    MapValue map_value                  // Nested map
    string field_reference_value        // Field reference (pipeline queries)
    string variable_reference_value     // Variable reference (pipeline queries)
    Function function_value             // Unevaluated expression (pipeline)
    Pipeline pipeline_value             // Unevaluated pipeline (pipeline)
  }
}
```

### Write Operation

```
Write {
  oneof operation {
    Document update                     // Create/update a document
    string delete                       // Delete by document name
    DocumentTransform transform         // Apply field transforms
  }
  DocumentMask update_mask              // Fields to update (for update operations)
  FieldTransform[] update_transforms    // Transforms to apply after update
  Precondition current_document         // Optional precondition
}
```

### FieldTransform Types

| Transform | Description |
|-----------|-------------|
| `set_to_server_value` | Sets to server value (e.g., `REQUEST_TIME` for server timestamp) |
| `increment` | Atomic increment (integer or double) |
| `maximum` | Sets to max of current and given value |
| `minimum` | Sets to min of current and given value |
| `append_missing_elements` | Appends elements to array if not present |
| `remove_all_from_array` | Removes all matching elements from array |

### StructuredQuery

```
StructuredQuery {
  Projection select                     // Fields to return
  CollectionSelector[] from             // Collections to query
  Filter where                          // Filters (composite, field, unary)
  Order[] order_by                      // Sort ordering
  Cursor start_at                       // Start cursor
  Cursor end_at                         // End cursor
  int32 offset                          // Skip N results
  Int32Value limit                      // Max results
  FindNearest find_nearest              // Vector similarity search
}
```

### Filter Operators

**Field filter operators:** `LESS_THAN`, `LESS_THAN_OR_EQUAL`, `GREATER_THAN`, `GREATER_THAN_OR_EQUAL`, `EQUAL`, `NOT_EQUAL`, `ARRAY_CONTAINS`, `IN`, `ARRAY_CONTAINS_ANY`, `NOT_IN`

**Unary filter operators:** `IS_NAN`, `IS_NULL`, `IS_NOT_NAN`, `IS_NOT_NULL`

**Composite filter operators:** `AND`, `OR`

### Aggregation Types

| Aggregation | Description |
|-------------|-------------|
| `Count` | Count of matching documents (with optional `up_to` limit) |
| `Sum` | Sum of a numeric field |
| `Avg` | Average of a numeric field |

### TransactionOptions

```
TransactionOptions {
  oneof mode {
    ReadOnly read_only    // Read-only transaction (optional read_time for snapshots)
    ReadWrite read_write  // Read-write transaction (optional retry_transaction)
  }
}
```

### Consistency Selectors

Most read operations support one of:
- `transaction` (bytes) — Read within an existing transaction
- `new_transaction` (TransactionOptions) — Start a new transaction for this read
- `read_time` (Timestamp) — Read at a specific point in time

---

## Listen Protocol (Real-time Updates)

The `Listen` RPC uses bidirectional streaming with a complex protocol:

### Client → Server (ListenRequest)
- `add_target` — Add a target (query or document set) to watch
- `remove_target` — Remove a target by ID

### Server → Client (ListenResponse)
- `target_change` — Target state changes (ADD, REMOVE, CURRENT, RESET, NO_CHANGE)
- `document_change` — A document was created or modified
- `document_delete` — A document was deleted
- `document_remove` — A document is no longer relevant to a target
- `filter` — ExistenceFilter with optional BloomFilter for efficient reconciliation

### Target Types
- **QueryTarget** — Watch results of a StructuredQuery
- **DocumentsTarget** — Watch specific documents by name

### Resume Mechanism
Targets support resume via `resume_token` or `read_time` to reconnect without replaying the full state.

---

## Resource Path Format

All Firestore resources follow this naming convention:
```
projects/{project_id}/databases/{database_id}/documents/{document_path}
```

- Database ID is typically `(default)`
- Document paths alternate between collection and document IDs: `collection/doc/subcollection/subdoc`

---

## Priority for Implementation

### Phase 1 — Core CRUD (minimum viable emulator)
1. `GetDocument`
2. `CreateDocument`
3. `UpdateDocument`
4. `DeleteDocument`
5. `ListDocuments`
6. `BatchGetDocuments`
7. `ListCollectionIds`

### Phase 2 — Queries and Transactions
8. `RunQuery` (with StructuredQuery support)
9. `BeginTransaction`
10. `Commit`
11. `Rollback`
12. `BatchWrite`

### Phase 3 — Advanced Queries
13. `RunAggregationQuery`
14. `PartitionQuery`
15. `ExecutePipeline`

### Phase 4 — Real-time and Streaming
16. `Listen` (bidirectional streaming, most complex)
17. `Write` (bidirectional streaming)

### Emulator-specific Endpoints (non-gRPC, REST)
The official Firebase emulator also provides:
- `DELETE /emulator/v1/projects/{project}/databases/{db}/documents` — Clear all data
- `GET /emulator/v1/projects/{project}:ruleCoverage` — Security rules coverage
