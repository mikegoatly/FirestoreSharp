using System.Text.Json.Serialization;

namespace FirestoreSharp.Server.UI;

[JsonSerializable(typeof(CollectionListResponse))]
[JsonSerializable(typeof(DocumentListResponse))]
[JsonSerializable(typeof(DocumentResponse))]
[JsonSerializable(typeof(CreateDocumentRequest))]
[JsonSerializable(typeof(UpdateDocumentRequest))]
[JsonSerializable(typeof(ConfigResponse))]
[JsonSerializable(typeof(DatabaseInfo))]
[JsonSerializable(typeof(UiValue))]
[JsonSerializable(typeof(UiGeoPoint))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, UiValue>))]
[JsonSerializable(typeof(IReadOnlyList<UiValue>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class FirestoreJsonContext : JsonSerializerContext
{
}
