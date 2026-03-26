using FirestoreSharp.Core.Query;
using FirestoreSharp.Tests.Unit.Builders;
using Google.Cloud.Firestore.V1;
using Xunit;

using Value = Google.Cloud.Firestore.V1.Value;
using FieldOp = Google.Cloud.Firestore.V1.StructuredQuery.Types.FieldFilter.Types.Operator;
using UnaryOp = Google.Cloud.Firestore.V1.StructuredQuery.Types.UnaryFilter.Types.Operator;

namespace FirestoreSharp.Tests.Unit.Query;

public sealed class QueryFilterTests
{
    // ── Field operators: EQUAL / NOT_EQUAL ─────────────────────────────────

    [Fact]
    public void Equal_MatchingString_ReturnsTrue()
    {
        var doc = Doc("status", Str("active"));
        Assert.True(QueryFilter.Matches(doc, FieldFilter("status", FieldOp.Equal, Str("active"))));
    }

    [Fact]
    public void Equal_NonMatchingString_ReturnsFalse()
    {
        var doc = Doc("status", Str("inactive"));
        Assert.False(QueryFilter.Matches(doc, FieldFilter("status", FieldOp.Equal, Str("active"))));
    }

    [Fact]
    public void Equal_MissingField_ReturnsFalse()
    {
        var doc = Doc("other", Str("x"));
        Assert.False(QueryFilter.Matches(doc, FieldFilter("status", FieldOp.Equal, Str("active"))));
    }

    [Fact]
    public void NotEqual_MatchingValue_ReturnsFalse()
    {
        var doc = Doc("score", Int(10));
        Assert.False(QueryFilter.Matches(doc, FieldFilter("score", FieldOp.NotEqual, Int(10))));
    }

    [Fact]
    public void NotEqual_DifferentValue_ReturnsTrue()
    {
        var doc = Doc("score", Int(5));
        Assert.True(QueryFilter.Matches(doc, FieldFilter("score", FieldOp.NotEqual, Int(10))));
    }

    [Fact]
    public void NotEqual_NaN_ReturnsFalse()
    {
        // NaN != NaN is always false in Firestore (NOT_EQUAL never matches NaN)
        var doc = Doc("x", Dbl(double.NaN));
        Assert.False(QueryFilter.Matches(doc, FieldFilter("x", FieldOp.NotEqual, Int(5))));
    }

    // ── Inequality operators ───────────────────────────────────────────────

    [Fact]
    public void LessThan_WhenLess_ReturnsTrue()
    {
        var doc = Doc("age", Int(20));
        Assert.True(QueryFilter.Matches(doc, FieldFilter("age", FieldOp.LessThan, Int(30))));
    }

    [Fact]
    public void LessThan_WhenEqual_ReturnsFalse()
    {
        var doc = Doc("age", Int(30));
        Assert.False(QueryFilter.Matches(doc, FieldFilter("age", FieldOp.LessThan, Int(30))));
    }

    [Fact]
    public void LessThan_NaN_ReturnsFalse()
    {
        var doc = Doc("x", Dbl(double.NaN));
        Assert.False(QueryFilter.Matches(doc, FieldFilter("x", FieldOp.LessThan, Dbl(0))));
    }

    [Fact]
    public void LessThanOrEqual_WhenEqual_ReturnsTrue()
    {
        var doc = Doc("age", Int(30));
        Assert.True(QueryFilter.Matches(doc, FieldFilter("age", FieldOp.LessThanOrEqual, Int(30))));
    }

    [Fact]
    public void GreaterThan_WhenGreater_ReturnsTrue()
    {
        var doc = Doc("price", Dbl(99.99));
        Assert.True(QueryFilter.Matches(doc, FieldFilter("price", FieldOp.GreaterThan, Dbl(50.0))));
    }

    [Fact]
    public void GreaterThanOrEqual_WhenEqual_ReturnsTrue()
    {
        var doc = Doc("price", Dbl(50.0));
        Assert.True(QueryFilter.Matches(doc, FieldFilter("price", FieldOp.GreaterThanOrEqual, Dbl(50.0))));
    }

    // ── IN / NOT_IN ───────────────────────────────────────────────────────

    [Fact]
    public void In_ValueInArray_ReturnsTrue()
    {
        var doc = Doc("status", Str("active"));
        var filter = FieldFilter("status", FieldOp.In, ArrayVal(Str("active"), Str("pending")));
        Assert.True(QueryFilter.Matches(doc, filter));
    }

    [Fact]
    public void In_ValueNotInArray_ReturnsFalse()
    {
        var doc = Doc("status", Str("archived"));
        var filter = FieldFilter("status", FieldOp.In, ArrayVal(Str("active"), Str("pending")));
        Assert.False(QueryFilter.Matches(doc, filter));
    }

    [Fact]
    public void NotIn_ValueNotInArray_ReturnsTrue()
    {
        var doc = Doc("status", Str("deleted"));
        var filter = FieldFilter("status", FieldOp.NotIn, ArrayVal(Str("active"), Str("pending")));
        Assert.True(QueryFilter.Matches(doc, filter));
    }

    [Fact]
    public void NotIn_ValueInArray_ReturnsFalse()
    {
        var doc = Doc("status", Str("active"));
        var filter = FieldFilter("status", FieldOp.NotIn, ArrayVal(Str("active"), Str("pending")));
        Assert.False(QueryFilter.Matches(doc, filter));
    }

    [Fact]
    public void NotIn_MissingField_ReturnsFalse()
    {
        var doc = Doc("other", Str("x"));
        var filter = FieldFilter("status", FieldOp.NotIn, ArrayVal(Str("active")));
        Assert.False(QueryFilter.Matches(doc, filter));
    }

    // ── ARRAY_CONTAINS / ARRAY_CONTAINS_ANY ──────────────────────────────

    [Fact]
    public void ArrayContains_ElementInArray_ReturnsTrue()
    {
        var doc = Doc("tags", ArrayVal(Str("a"), Str("b")));
        Assert.True(QueryFilter.Matches(doc, FieldFilter("tags", FieldOp.ArrayContains, Str("a"))));
    }

    [Fact]
    public void ArrayContains_ElementMissing_ReturnsFalse()
    {
        var doc = Doc("tags", ArrayVal(Str("a"), Str("b")));
        Assert.False(QueryFilter.Matches(doc, FieldFilter("tags", FieldOp.ArrayContains, Str("c"))));
    }

    [Fact]
    public void ArrayContains_FieldIsNotArray_ReturnsFalse()
    {
        var doc = Doc("tags", Str("a"));
        Assert.False(QueryFilter.Matches(doc, FieldFilter("tags", FieldOp.ArrayContains, Str("a"))));
    }

    [Fact]
    public void ArrayContainsAny_AnyMatchingElement_ReturnsTrue()
    {
        var doc = Doc("tags", ArrayVal(Str("sports"), Str("news")));
        var filter = FieldFilter("tags", FieldOp.ArrayContainsAny, ArrayVal(Str("sports"), Str("tech")));
        Assert.True(QueryFilter.Matches(doc, filter));
    }

    [Fact]
    public void ArrayContainsAny_NoMatchingElement_ReturnsFalse()
    {
        var doc = Doc("tags", ArrayVal(Str("cooking")));
        var filter = FieldFilter("tags", FieldOp.ArrayContainsAny, ArrayVal(Str("sports"), Str("tech")));
        Assert.False(QueryFilter.Matches(doc, filter));
    }

    // ── Unary operators ───────────────────────────────────────────────────

    [Fact]
    public void IsNull_NullValue_ReturnsTrue()
    {
        var doc = Doc("field", Null());
        Assert.True(QueryFilter.Matches(doc, UnaryFilter("field", UnaryOp.IsNull)));
    }

    [Fact]
    public void IsNull_NonNullValue_ReturnsFalse()
    {
        var doc = Doc("field", Str("value"));
        Assert.False(QueryFilter.Matches(doc, UnaryFilter("field", UnaryOp.IsNull)));
    }

    [Fact]
    public void IsNotNull_NonNullValue_ReturnsTrue()
    {
        var doc = Doc("field", Str("value"));
        Assert.True(QueryFilter.Matches(doc, UnaryFilter("field", UnaryOp.IsNotNull)));
    }

    [Fact]
    public void IsNotNull_NullValue_ReturnsFalse()
    {
        var doc = Doc("field", Null());
        Assert.False(QueryFilter.Matches(doc, UnaryFilter("field", UnaryOp.IsNotNull)));
    }

    [Fact]
    public void IsNan_NaNValue_ReturnsTrue()
    {
        var doc = Doc("score", Dbl(double.NaN));
        Assert.True(QueryFilter.Matches(doc, UnaryFilter("score", UnaryOp.IsNan)));
    }

    [Fact]
    public void IsNan_NonNaNValue_ReturnsFalse()
    {
        var doc = Doc("score", Dbl(1.5));
        Assert.False(QueryFilter.Matches(doc, UnaryFilter("score", UnaryOp.IsNan)));
    }

    [Fact]
    public void IsNotNan_NonNaNValue_ReturnsTrue()
    {
        var doc = Doc("score", Dbl(1.5));
        Assert.True(QueryFilter.Matches(doc, UnaryFilter("score", UnaryOp.IsNotNan)));
    }

    [Fact]
    public void IsNotNan_NaNValue_ReturnsFalse()
    {
        var doc = Doc("score", Dbl(double.NaN));
        Assert.False(QueryFilter.Matches(doc, UnaryFilter("score", UnaryOp.IsNotNan)));
    }

    // ── Composite filters ─────────────────────────────────────────────────

    [Fact]
    public void CompositeAnd_BothMatch_ReturnsTrue()
    {
        var doc = new DocumentBuilder()
            .WithField("status", "active")
            .WithField("age", 25L)
            .Build();

        var filter = CompositeAnd(
            FieldFilter("status", FieldOp.Equal, Str("active")),
            FieldFilter("age", FieldOp.GreaterThan, Int(18)));

        Assert.True(QueryFilter.Matches(doc, filter));
    }

    [Fact]
    public void CompositeAnd_OneDoesNotMatch_ReturnsFalse()
    {
        var doc = new DocumentBuilder()
            .WithField("status", "active")
            .WithField("age", 10L)
            .Build();

        var filter = CompositeAnd(
            FieldFilter("status", FieldOp.Equal, Str("active")),
            FieldFilter("age", FieldOp.GreaterThan, Int(18)));

        Assert.False(QueryFilter.Matches(doc, filter));
    }

    [Fact]
    public void CompositeOr_OneMatches_ReturnsTrue()
    {
        var doc = Doc("status", Str("pending"));

        var filter = CompositeOr(
            FieldFilter("status", FieldOp.Equal, Str("active")),
            FieldFilter("status", FieldOp.Equal, Str("pending")));

        Assert.True(QueryFilter.Matches(doc, filter));
    }

    [Fact]
    public void CompositeOr_NoneMatch_ReturnsFalse()
    {
        var doc = Doc("status", Str("deleted"));

        var filter = CompositeOr(
            FieldFilter("status", FieldOp.Equal, Str("active")),
            FieldFilter("status", FieldOp.Equal, Str("pending")));

        Assert.False(QueryFilter.Matches(doc, filter));
    }

    [Fact]
    public void NestedComposite_WorksCorrectly()
    {
        // (status = "active" AND age > 18) OR (status = "vip")
        var doc = new DocumentBuilder()
            .WithField("status", "vip")
            .WithField("age", 10L)
            .Build();

        var filter = CompositeOr(
            CompositeAnd(
                FieldFilter("status", FieldOp.Equal, Str("active")),
                FieldFilter("age", FieldOp.GreaterThan, Int(18))),
            FieldFilter("status", FieldOp.Equal, Str("vip")));

        Assert.True(QueryFilter.Matches(doc, filter));
    }

    // ── __name__ pseudo-field ─────────────────────────────────────────────

    [Fact]
    public void NameField_EqualFilter_MatchesDocumentName()
    {
        var doc = new DocumentBuilder()
            .WithCollection("users")
            .WithId("alice")
            .Build();

        var filter = FieldFilter("__name__", FieldOp.Equal,
            new Value { ReferenceValue = "projects/test-project/databases/(default)/documents/users/alice" });

        Assert.True(QueryFilter.Matches(doc, filter));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static Document Doc(string field, Value value)
    {
        var doc = new DocumentBuilder().WithId(Guid.NewGuid().ToString()).Build();
        var path = FirestoreSharp.Core.FieldPath.Parse(field);
        FirestoreSharp.Core.DocumentNavigator.SetValue(doc, path, value);
        return doc;
    }

    private static Value Null() => new() { NullValue = Google.Protobuf.WellKnownTypes.NullValue.NullValue };
    private static Value Bool(bool v) => new() { BooleanValue = v };
    private static Value Int(long v) => new() { IntegerValue = v };
    private static Value Dbl(double v) => new() { DoubleValue = v };
    private static Value Str(string v) => new() { StringValue = v };

    private static Value ArrayVal(params Value[] values)
    {
        var av = new ArrayValue();
        av.Values.AddRange(values);
        return new Value { ArrayValue = av };
    }

    private static StructuredQuery.Types.Filter FieldFilter(
        string fieldPath,
        StructuredQuery.Types.FieldFilter.Types.Operator op,
        Value value)
    {
        return new StructuredQuery.Types.Filter
        {
            FieldFilter = new StructuredQuery.Types.FieldFilter
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = fieldPath },
                Op = op,
                Value = value
            }
        };
    }

    private static StructuredQuery.Types.Filter UnaryFilter(
        string fieldPath,
        StructuredQuery.Types.UnaryFilter.Types.Operator op)
    {
        return new StructuredQuery.Types.Filter
        {
            UnaryFilter = new StructuredQuery.Types.UnaryFilter
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = fieldPath },
                Op = op
            }
        };
    }

    private static StructuredQuery.Types.Filter CompositeAnd(params StructuredQuery.Types.Filter[] filters)
    {
        var composite = new StructuredQuery.Types.CompositeFilter
        {
            Op = StructuredQuery.Types.CompositeFilter.Types.Operator.And
        };
        composite.Filters.AddRange(filters);
        return new StructuredQuery.Types.Filter { CompositeFilter = composite };
    }

    private static StructuredQuery.Types.Filter CompositeOr(params StructuredQuery.Types.Filter[] filters)
    {
        var composite = new StructuredQuery.Types.CompositeFilter
        {
            Op = StructuredQuery.Types.CompositeFilter.Types.Operator.Or
        };
        composite.Filters.AddRange(filters);
        return new StructuredQuery.Types.Filter { CompositeFilter = composite };
    }
}
