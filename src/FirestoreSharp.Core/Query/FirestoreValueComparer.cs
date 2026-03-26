using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;

using Value = Google.Cloud.Firestore.V1.Value;

namespace FirestoreSharp.Core.Query;

/// <summary>
/// Compares <see cref="Value"/> instances using Firestore's defined ordering:
/// null &lt; bool &lt; number (int/double unified, NaN first) &lt; timestamp &lt; string &lt; bytes
/// &lt; reference &lt; geo_point &lt; array &lt; map.
/// </summary>
internal sealed class FirestoreValueComparer : IComparer<Value?>
{
    public static readonly FirestoreValueComparer Instance = new();

    private FirestoreValueComparer() { }

    public int Compare(Value? x, Value? y)
    {
        if (x is null && y is null)
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var typeX = GetTypeOrder(x);
        var typeY = GetTypeOrder(y);

        if (typeX != typeY)
        {
            return typeX.CompareTo(typeY);
        }

        return CompareWithinType(x, y);
    }

    private static int CompareWithinType(Value x, Value y)
    {
        return x.ValueTypeCase switch
        {
            Value.ValueTypeOneofCase.NullValue => 0,
            Value.ValueTypeOneofCase.BooleanValue => x.BooleanValue.CompareTo(y.BooleanValue),
            Value.ValueTypeOneofCase.IntegerValue => CompareNumbers(x, y),
            Value.ValueTypeOneofCase.DoubleValue => CompareNumbers(x, y),
            Value.ValueTypeOneofCase.TimestampValue => CompareTimestamps(x.TimestampValue, y.TimestampValue),
            Value.ValueTypeOneofCase.StringValue => string.Compare(x.StringValue, y.StringValue, StringComparison.Ordinal),
            Value.ValueTypeOneofCase.BytesValue => CompareBytes(x.BytesValue, y.BytesValue),
            Value.ValueTypeOneofCase.ReferenceValue => string.Compare(x.ReferenceValue, y.ReferenceValue, StringComparison.Ordinal),
            Value.ValueTypeOneofCase.GeoPointValue => CompareGeoPoints(x.GeoPointValue, y.GeoPointValue),
            Value.ValueTypeOneofCase.ArrayValue => CompareArrays(x.ArrayValue, y.ArrayValue),
            Value.ValueTypeOneofCase.MapValue => CompareMaps(x.MapValue, y.MapValue),
            _ => 0
        };
    }

    /// <summary>
    /// Comparisons between integers and doubles are done by converting to double.
    /// NaN is considered less than all other numbers (including -Infinity).
    /// </summary>
    private static int CompareNumbers(Value x, Value y)
    {
        var dx = ToDouble(x);
        var dy = ToDouble(y);

        // Both NaN → equal
        if (double.IsNaN(dx) && double.IsNaN(dy))
        {
            return 0;
        }

        // NaN is less than everything
        if (double.IsNaN(dx))
        {
            return -1;
        }

        if (double.IsNaN(dy))
        {
            return 1;
        }

        return dx.CompareTo(dy);
    }

    private static double ToDouble(Value v) =>
        v.ValueTypeCase == Value.ValueTypeOneofCase.IntegerValue
            ? (double)v.IntegerValue
            : v.DoubleValue;

    private static int CompareTimestamps(Timestamp x, Timestamp y)
    {
        var cmp = x.Seconds.CompareTo(y.Seconds);
        return cmp != 0 ? cmp : x.Nanos.CompareTo(y.Nanos);
    }

    private static int CompareBytes(Google.Protobuf.ByteString x, Google.Protobuf.ByteString y)
    {
        var minLen = Math.Min(x.Length, y.Length);
        for (var i = 0; i < minLen; i++)
        {
            var cmp = x[i].CompareTo(y[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }
        return x.Length.CompareTo(y.Length);
    }

    private static int CompareGeoPoints(Google.Type.LatLng x, Google.Type.LatLng y)
    {
        var cmp = x.Latitude.CompareTo(y.Latitude);
        return cmp != 0 ? cmp : x.Longitude.CompareTo(y.Longitude);
    }

    private static int CompareArrays(ArrayValue x, ArrayValue y)
    {
        var minLen = Math.Min(x.Values.Count, y.Values.Count);
        for (var i = 0; i < minLen; i++)
        {
            var cmp = Instance.Compare(x.Values[i], y.Values[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }
        return x.Values.Count.CompareTo(y.Values.Count);
    }

    private static int CompareMaps(MapValue x, MapValue y)
    {
        // Maps are compared by their sorted key-value pairs
        var xPairs = x.Fields.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        var yPairs = y.Fields.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();

        var minLen = Math.Min(xPairs.Count, yPairs.Count);
        for (var i = 0; i < minLen; i++)
        {
            var keyCmp = string.Compare(xPairs[i].Key, yPairs[i].Key, StringComparison.Ordinal);
            if (keyCmp != 0)
            {
                return keyCmp;
            }

            var valCmp = Instance.Compare(xPairs[i].Value, yPairs[i].Value);
            if (valCmp != 0)
            {
                return valCmp;
            }
        }

        return xPairs.Count.CompareTo(yPairs.Count);
    }

    /// <summary>
    /// Returns an integer representing the type's position in Firestore's cross-type ordering.
    /// Integers and doubles share the same type order (they are compared as numbers).
    /// </summary>
    private static int GetTypeOrder(Value v) => v.ValueTypeCase switch
    {
        Value.ValueTypeOneofCase.NullValue => 0,
        Value.ValueTypeOneofCase.BooleanValue => 1,
        Value.ValueTypeOneofCase.IntegerValue => 2,
        Value.ValueTypeOneofCase.DoubleValue => 2,
        Value.ValueTypeOneofCase.TimestampValue => 3,
        Value.ValueTypeOneofCase.StringValue => 4,
        Value.ValueTypeOneofCase.BytesValue => 5,
        Value.ValueTypeOneofCase.ReferenceValue => 6,
        Value.ValueTypeOneofCase.GeoPointValue => 7,
        Value.ValueTypeOneofCase.ArrayValue => 8,
        Value.ValueTypeOneofCase.MapValue => 9,
        _ => -1
    };

    /// <summary>
    /// Returns true when <paramref name="v"/> is NaN (DoubleValue with double.NaN).
    /// Integers can never be NaN.
    /// </summary>
    public static bool IsNaN(Value v) =>
        v.ValueTypeCase == Value.ValueTypeOneofCase.DoubleValue && double.IsNaN(v.DoubleValue);

    /// <summary>
    /// Returns true when <paramref name="v"/> is a null value.
    /// </summary>
    public static bool IsNull(Value v) =>
        v.ValueTypeCase == Value.ValueTypeOneofCase.NullValue;
}
