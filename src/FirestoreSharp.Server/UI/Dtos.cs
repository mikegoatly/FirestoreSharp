using System.Text.Json;

namespace FirestoreSharp.Server.UI;

// UiValue is a tagged union representing any Firestore field value.
// Type strings: "null", "bool", "int", "double", "timestamp", "string",
//               "bytes", "reference", "geopoint", "array", "map"
// Value is a JsonElement so STJ source generation works cleanly under AOT.
// int64 values are serialized as JSON strings to preserve precision in JS.
internal sealed record UiValue(string Type, JsonElement Value);

internal sealed record UiGeoPoint(double Latitude, double Longitude);

internal sealed record CollectionListResponse(IReadOnlyList<string> CollectionIds, string? NextPageToken);

internal sealed record DocumentSummary(
    string ResourceName,
    string DocumentId,
    IReadOnlyDictionary<string, UiValue> Fields,
    string? CreateTime,
    string? UpdateTime);

internal sealed record DocumentListResponse(IReadOnlyList<DocumentSummary> Documents, string? NextPageToken);

internal sealed record DocumentResponse(
    string ResourceName,
    string DocumentId,
    IReadOnlyDictionary<string, UiValue> Fields,
    string? CreateTime,
    string? UpdateTime);

internal sealed record CreateDocumentRequest(string? DocumentId, IReadOnlyDictionary<string, UiValue>? Fields);

internal sealed record UpdateDocumentRequest(
    IReadOnlyDictionary<string, UiValue>? Fields,
    IReadOnlyList<string>? UpdateMask);

internal sealed record ConfigResponse(string Project, string Database);
