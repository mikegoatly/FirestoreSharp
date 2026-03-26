using FirestoreSharp.Core.Query;
using Google.Cloud.Firestore.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Xunit;

using Value = Google.Cloud.Firestore.V1.Value;

namespace FirestoreSharp.Tests.Unit.Query;

public sealed class FirestoreValueComparerTests
{
    private static readonly FirestoreValueComparer Comparer = FirestoreValueComparer.Instance;

    // ── Cross-type ordering ────────────────────────────────────────────────

    [Fact]
    public void Null_IsLessThan_Bool()
    {
        var result = Comparer.Compare(Null(), Bool(false));
        Assert.True(result < 0);
    }

    [Fact]
    public void Bool_IsLessThan_Integer()
    {
        var result = Comparer.Compare(Bool(true), Int(0));
        Assert.True(result < 0);
    }

    [Fact]
    public void Integer_IsLessThan_Timestamp()
    {
        var result = Comparer.Compare(Int(long.MaxValue), Ts(1, 0));
        Assert.True(result < 0);
    }

    [Fact]
    public void Timestamp_IsLessThan_String()
    {
        var result = Comparer.Compare(Ts(9999, 0), Str("a"));
        Assert.True(result < 0);
    }

    [Fact]
    public void String_IsLessThan_Bytes()
    {
        var result = Comparer.Compare(Str("zzz"), Bytes([0x00]));
        Assert.True(result < 0);
    }

    [Fact]
    public void Bytes_IsLessThan_Reference()
    {
        var result = Comparer.Compare(Bytes([0xFF]), Ref("projects/p/databases/d/documents/col/doc"));
        Assert.True(result < 0);
    }

    [Fact]
    public void Reference_IsLessThan_GeoPoint()
    {
        var result = Comparer.Compare(Ref("projects/p/databases/d/documents/z"), Geo(0, 0));
        Assert.True(result < 0);
    }

    [Fact]
    public void GeoPoint_IsLessThan_Array()
    {
        var result = Comparer.Compare(Geo(90, 180), Array(Null()));
        Assert.True(result < 0);
    }

    [Fact]
    public void Array_IsLessThan_Map()
    {
        var result = Comparer.Compare(Array(Null()), Map("k", Null()));
        Assert.True(result < 0);
    }

    // ── Boolean ordering ───────────────────────────────────────────────────

    [Fact]
    public void Bool_False_IsLessThan_True()
    {
        Assert.True(Comparer.Compare(Bool(false), Bool(true)) < 0);
    }

    [Fact]
    public void Bool_True_Equals_True()
    {
        Assert.Equal(0, Comparer.Compare(Bool(true), Bool(true)));
    }

    // ── Numeric ordering ───────────────────────────────────────────────────

    [Fact]
    public void Integer_LessThan_Integer()
    {
        Assert.True(Comparer.Compare(Int(1), Int(2)) < 0);
    }

    [Fact]
    public void Double_LessThan_Double()
    {
        Assert.True(Comparer.Compare(Dbl(1.5), Dbl(2.5)) < 0);
    }

    [Fact]
    public void Integer_And_EquivalentDouble_AreEqual()
    {
        Assert.Equal(0, Comparer.Compare(Int(5), Dbl(5.0)));
    }

    [Fact]
    public void NaN_IsLessThan_NegativeInfinity()
    {
        Assert.True(Comparer.Compare(Dbl(double.NaN), Dbl(double.NegativeInfinity)) < 0);
    }

    [Fact]
    public void NaN_Equals_NaN()
    {
        Assert.Equal(0, Comparer.Compare(Dbl(double.NaN), Dbl(double.NaN)));
    }

    [Fact]
    public void NaN_IsLessThan_Zero()
    {
        Assert.True(Comparer.Compare(Dbl(double.NaN), Int(0)) < 0);
    }

    [Fact]
    public void NaN_IsLessThan_MinInt()
    {
        Assert.True(Comparer.Compare(Dbl(double.NaN), Int(long.MinValue)) < 0);
    }

    // ── Timestamp ordering ─────────────────────────────────────────────────

    [Fact]
    public void Timestamp_EarlierSeconds_IsLess()
    {
        Assert.True(Comparer.Compare(Ts(100, 0), Ts(101, 0)) < 0);
    }

    [Fact]
    public void Timestamp_SameSeconds_EarlierNanos_IsLess()
    {
        Assert.True(Comparer.Compare(Ts(100, 0), Ts(100, 1)) < 0);
    }

    [Fact]
    public void Timestamp_Identical_AreEqual()
    {
        Assert.Equal(0, Comparer.Compare(Ts(100, 500), Ts(100, 500)));
    }

    // ── String ordering ────────────────────────────────────────────────────

    [Fact]
    public void String_Lexicographic_Ordering()
    {
        Assert.True(Comparer.Compare(Str("abc"), Str("abd")) < 0);
        Assert.True(Comparer.Compare(Str("abc"), Str("abcd")) < 0);
        Assert.Equal(0, Comparer.Compare(Str("abc"), Str("abc")));
    }

    // ── Bytes ordering ─────────────────────────────────────────────────────

    [Fact]
    public void Bytes_LexicographicByByte()
    {
        Assert.True(Comparer.Compare(Bytes([0x01]), Bytes([0x02])) < 0);
        Assert.True(Comparer.Compare(Bytes([0x01]), Bytes([0x01, 0x00])) < 0);
        Assert.Equal(0, Comparer.Compare(Bytes([0x01, 0x02]), Bytes([0x01, 0x02])));
    }

    // ── Reference ordering ─────────────────────────────────────────────────

    [Fact]
    public void Reference_OrdinalStringComparison()
    {
        Assert.True(Comparer.Compare(Ref("a/b"), Ref("a/c")) < 0);
    }

    // ── GeoPoint ordering ──────────────────────────────────────────────────

    [Fact]
    public void GeoPoint_OrderedByLatitudeThenLongitude()
    {
        Assert.True(Comparer.Compare(Geo(0, 0), Geo(1, 0)) < 0);
        Assert.True(Comparer.Compare(Geo(0, -1), Geo(0, 1)) < 0);
        Assert.Equal(0, Comparer.Compare(Geo(45, 90), Geo(45, 90)));
    }

    // ── Array ordering ─────────────────────────────────────────────────────

    [Fact]
    public void Array_ElementByElement_Shorter_IsLess_WhenPrefixEqual()
    {
        Assert.True(Comparer.Compare(Array(Int(1)), Array(Int(1), Int(2))) < 0);
    }

    [Fact]
    public void Array_FirstElementDeterminesOrder()
    {
        Assert.True(Comparer.Compare(Array(Int(1)), Array(Int(2))) < 0);
    }

    // ── Map ordering ───────────────────────────────────────────────────────

    [Fact]
    public void Map_OrderedBySortedKeys()
    {
        var x = Map("a", Int(1), "b", Int(2));
        var y = Map("a", Int(1), "b", Int(3));
        Assert.True(Comparer.Compare(x, y) < 0);
    }

    [Fact]
    public void Map_SmallerMap_IsLess_WhenPrefixEqual()
    {
        var x = Map("a", Int(1));
        var y = Map("a", Int(1), "b", Int(2));
        Assert.True(Comparer.Compare(x, y) < 0);
    }

    // ── Null checks ────────────────────────────────────────────────────────

    [Fact]
    public void IsNull_TrueForNullValue()
    {
        Assert.True(FirestoreValueComparer.IsNull(Null()));
    }

    [Fact]
    public void IsNull_FalseForOtherTypes()
    {
        Assert.False(FirestoreValueComparer.IsNull(Int(0)));
    }

    // ── NaN checks ────────────────────────────────────────────────────────

    [Fact]
    public void IsNaN_TrueForNaN()
    {
        Assert.True(FirestoreValueComparer.IsNaN(Dbl(double.NaN)));
    }

    [Fact]
    public void IsNaN_FalseForNonNaN()
    {
        Assert.False(FirestoreValueComparer.IsNaN(Dbl(1.0)));
        Assert.False(FirestoreValueComparer.IsNaN(Int(1)));
    }

    // ── Symmetry / reflexivity ─────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public void Compare_Reflexive_ReturnsZero(int _)
    {
        // Test one representative per type
        Assert.Equal(0, Comparer.Compare(Null(), Null()));
        Assert.Equal(0, Comparer.Compare(Bool(true), Bool(true)));
        Assert.Equal(0, Comparer.Compare(Int(42), Int(42)));
        Assert.Equal(0, Comparer.Compare(Dbl(3.14), Dbl(3.14)));
        Assert.Equal(0, Comparer.Compare(Str("hello"), Str("hello")));
    }

    // ── Factories ─────────────────────────────────────────────────────────

    private static Value Null() => new() { NullValue = Google.Protobuf.WellKnownTypes.NullValue.NullValue };
    private static Value Bool(bool v) => new() { BooleanValue = v };
    private static Value Int(long v) => new() { IntegerValue = v };
    private static Value Dbl(double v) => new() { DoubleValue = v };
    private static Value Str(string v) => new() { StringValue = v };
    private static Value Bytes(byte[] v) => new() { BytesValue = ByteString.CopyFrom(v) };
    private static Value Ref(string v) => new() { ReferenceValue = v };
    private static Value Ts(long seconds, int nanos) => new() { TimestampValue = new Timestamp { Seconds = seconds, Nanos = nanos } };
    private static Value Geo(double lat, double lng) => new() { GeoPointValue = new Google.Type.LatLng { Latitude = lat, Longitude = lng } };
    private static Value Array(params Value[] values)
    {
        var av = new ArrayValue();
        av.Values.AddRange(values);
        return new Value { ArrayValue = av };
    }
    private static Value Map(string k1, Value v1)
    {
        var mv = new MapValue();
        mv.Fields[k1] = v1;
        return new Value { MapValue = mv };
    }
    private static Value Map(string k1, Value v1, string k2, Value v2)
    {
        var mv = new MapValue();
        mv.Fields[k1] = v1;
        mv.Fields[k2] = v2;
        return new Value { MapValue = mv };
    }
}
