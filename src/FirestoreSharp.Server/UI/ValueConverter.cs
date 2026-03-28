using System.Globalization;
using System.Text.Json;
using Google.Cloud.Firestore.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using FirestoreValue = Google.Cloud.Firestore.V1.Value;

namespace FirestoreSharp.Server.UI;

internal static class ValueConverter
{
    private static readonly JsonElement JsonNull = JsonDocument.Parse("null").RootElement.Clone();
    private static readonly JsonElement JsonTrue = JsonDocument.Parse("true").RootElement.Clone();
    private static readonly JsonElement JsonFalse = JsonDocument.Parse("false").RootElement.Clone();

    public static UiValue ToUiValue(FirestoreValue v)
    {
        return v.ValueTypeCase switch
        {
            FirestoreValue.ValueTypeOneofCase.NullValue =>
                new UiValue("null", JsonNull),

            FirestoreValue.ValueTypeOneofCase.BooleanValue =>
                new UiValue("bool", v.BooleanValue ? JsonTrue : JsonFalse),

            FirestoreValue.ValueTypeOneofCase.IntegerValue =>
                // Serialize as string to preserve int64 precision in JavaScript
                new UiValue("int", StringElement(v.IntegerValue.ToString(CultureInfo.InvariantCulture))),

            FirestoreValue.ValueTypeOneofCase.DoubleValue =>
                SerializeDouble(v.DoubleValue),

            FirestoreValue.ValueTypeOneofCase.TimestampValue =>
                new UiValue("timestamp", StringElement(v.TimestampValue.ToDateTimeOffset().ToString("O"))),

            FirestoreValue.ValueTypeOneofCase.StringValue =>
                new UiValue("string", StringElement(v.StringValue)),

            FirestoreValue.ValueTypeOneofCase.BytesValue =>
                new UiValue("bytes", StringElement(Convert.ToBase64String(v.BytesValue.ToByteArray()))),

            FirestoreValue.ValueTypeOneofCase.ReferenceValue =>
                new UiValue("reference", StringElement(v.ReferenceValue)),

            FirestoreValue.ValueTypeOneofCase.GeoPointValue =>
                new UiValue("geopoint", JsonSerializer.SerializeToElement(
                    new UiGeoPoint(v.GeoPointValue.Latitude, v.GeoPointValue.Longitude),
                    FirestoreJsonContext.Default.UiGeoPoint)),

            FirestoreValue.ValueTypeOneofCase.ArrayValue =>
                SerializeArray(v.ArrayValue),

            FirestoreValue.ValueTypeOneofCase.MapValue =>
                SerializeMap(v.MapValue),

            _ => new UiValue("null", JsonNull)
        };
    }

    public static FirestoreValue FromUiValue(UiValue uv)
    {
        return uv.Type switch
        {
            "null" => new FirestoreValue { NullValue = NullValue.NullValue },

            "bool" => new FirestoreValue { BooleanValue = uv.Value.GetBoolean() },

            "int" => new FirestoreValue
            {
                IntegerValue = uv.Value.ValueKind == JsonValueKind.String
                    ? long.Parse(uv.Value.GetString()!, CultureInfo.InvariantCulture)
                    : uv.Value.GetInt64()
            },

            "double" => new FirestoreValue
            {
                DoubleValue = uv.Value.ValueKind == JsonValueKind.String
                    ? ParseSpecialDouble(uv.Value.GetString()!)
                    : uv.Value.GetDouble()
            },

            "timestamp" => new FirestoreValue
            {
                TimestampValue = Timestamp.FromDateTimeOffset(
                    DateTimeOffset.Parse(uv.Value.GetString()!, CultureInfo.InvariantCulture))
            },

            "string" => new FirestoreValue { StringValue = uv.Value.GetString()! },

            "bytes" => new FirestoreValue
            {
                BytesValue = ByteString.CopyFrom(Convert.FromBase64String(uv.Value.GetString()!))
            },

            "reference" => new FirestoreValue { ReferenceValue = uv.Value.GetString()! },

            "geopoint" => DeserializeGeoPoint(uv.Value),

            "array" => DeserializeArray(uv.Value),

            "map" => DeserializeMap(uv.Value),

            _ => new FirestoreValue { NullValue = NullValue.NullValue }
        };
    }

    private static JsonElement StringElement(string s)
    {
        // Build a JSON string element by serializing a string value
        return JsonSerializer.SerializeToElement(s, FirestoreJsonContext.Default.String);
    }

    private static UiValue SerializeDouble(double d)
    {
        if (double.IsNaN(d))
            return new UiValue("double", StringElement("NaN"));
        if (double.IsPositiveInfinity(d))
            return new UiValue("double", StringElement("Infinity"));
        if (double.IsNegativeInfinity(d))
            return new UiValue("double", StringElement("-Infinity"));
        return new UiValue("double", JsonSerializer.SerializeToElement(d, FirestoreJsonContext.Default.Double));
    }

    private static UiValue SerializeArray(ArrayValue array)
    {
        var items = array.Values.Select(ToUiValue).ToList();
        return new UiValue("array", JsonSerializer.SerializeToElement(items, FirestoreJsonContext.Default.IReadOnlyListUiValue));
    }

    private static UiValue SerializeMap(MapValue map)
    {
        var dict = map.Fields.ToDictionary(kv => kv.Key, kv => ToUiValue(kv.Value));
        return new UiValue("map", JsonSerializer.SerializeToElement(
            (IReadOnlyDictionary<string, UiValue>)dict,
            FirestoreJsonContext.Default.IReadOnlyDictionaryStringUiValue));
    }

    private static double ParseSpecialDouble(string s) => s switch
    {
        "NaN" => double.NaN,
        "Infinity" => double.PositiveInfinity,
        "-Infinity" => double.NegativeInfinity,
        _ => double.Parse(s, CultureInfo.InvariantCulture)
    };

    private static FirestoreValue DeserializeGeoPoint(JsonElement el)
    {
        var geoPoint = JsonSerializer.Deserialize(el, FirestoreJsonContext.Default.UiGeoPoint)!;
        return new FirestoreValue
        {
            GeoPointValue = new Google.Type.LatLng
            {
                Latitude = geoPoint.Latitude,
                Longitude = geoPoint.Longitude
            }
        };
    }

    private static FirestoreValue DeserializeArray(JsonElement el)
    {
        var array = new ArrayValue();
        foreach (var item in el.EnumerateArray())
        {
            var uiVal = JsonSerializer.Deserialize(item, FirestoreJsonContext.Default.UiValue)!;
            array.Values.Add(FromUiValue(uiVal));
        }
        return new FirestoreValue { ArrayValue = array };
    }

    private static FirestoreValue DeserializeMap(JsonElement el)
    {
        var map = new MapValue();
        foreach (var prop in el.EnumerateObject())
        {
            var uiVal = JsonSerializer.Deserialize(prop.Value, FirestoreJsonContext.Default.UiValue)!;
            map.Fields[prop.Name] = FromUiValue(uiVal);
        }
        return new FirestoreValue { MapValue = map };
    }
}
