using FirestoreSharp.Core.Query;
using FirestoreSharp.Tests.Unit.Builders;
using Google.Cloud.Firestore.V1;
using Xunit;

using Value = Google.Cloud.Firestore.V1.Value;

namespace FirestoreSharp.Tests.Unit.Query;

public sealed class QueryOrdererTests
{
    private const string Parent = "projects/test-project/databases/(default)/documents";

    // ── Basic ascending / descending ──────────────────────────────────────

    [Fact]
    public void Sort_Ascending_OrdersByFieldAscending()
    {
        var docs = new[]
        {
            DocWithField("score", Int(30), "c"),
            DocWithField("score", Int(10), "a"),
            DocWithField("score", Int(20), "b"),
        };

        var result = QueryOrderer.Sort(docs, [Order("score", Asc)], null);

        Assert.Equal(10L, result[0].Fields["score"].IntegerValue);
        Assert.Equal(20L, result[1].Fields["score"].IntegerValue);
        Assert.Equal(30L, result[2].Fields["score"].IntegerValue);
    }

    [Fact]
    public void Sort_Descending_OrdersByFieldDescending()
    {
        var docs = new[]
        {
            DocWithField("score", Int(10), "a"),
            DocWithField("score", Int(30), "c"),
            DocWithField("score", Int(20), "b"),
        };

        var result = QueryOrderer.Sort(docs, [Order("score", Desc)], null);

        Assert.Equal(30L, result[0].Fields["score"].IntegerValue);
        Assert.Equal(20L, result[1].Fields["score"].IntegerValue);
        Assert.Equal(10L, result[2].Fields["score"].IntegerValue);
    }

    // ── Implicit __name__ appending ───────────────────────────────────────

    [Fact]
    public void Sort_WithNoOrderBy_ImplicitNameAppended_StableByResourceName()
    {
        var docs = new[]
        {
            NamedDoc("c"),
            NamedDoc("a"),
            NamedDoc("b"),
        };

        // No explicit orders — should sort by __name__ ascending
        var result = QueryOrderer.Sort(docs, [], null);

        Assert.EndsWith("/a", result[0].Name, StringComparison.Ordinal);
        Assert.EndsWith("/b", result[1].Name, StringComparison.Ordinal);
        Assert.EndsWith("/c", result[2].Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Sort_FieldThenImplicitName_BreaksTiesWithName()
    {
        // Two docs with the same score — name should break the tie
        var docs = new[]
        {
            DocWithField("score", Int(10), "z"),
            DocWithField("score", Int(10), "a"),
        };

        var result = QueryOrderer.Sort(docs, [Order("score", Asc)], null);

        Assert.EndsWith("/a", result[0].Name, StringComparison.Ordinal);
        Assert.EndsWith("/z", result[1].Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Sort_DescendingLastField_ImplicitNameIsAlsoDescending()
    {
        // If the last explicit order is DESC, __name__ should also be DESC
        var docs = new[]
        {
            DocWithField("score", Int(10), "a"),
            DocWithField("score", Int(10), "z"),
        };

        var result = QueryOrderer.Sort(docs, [Order("score", Desc)], null);

        // Both have score=10, name tiebreak is DESC: "z" < "a" in descending
        Assert.EndsWith("/z", result[0].Name, StringComparison.Ordinal);
        Assert.EndsWith("/a", result[1].Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Sort_ExplicitNameOrder_NoExtraNameAppended()
    {
        var docs = new[]
        {
            NamedDoc("c"),
            NamedDoc("a"),
        };

        var effective = QueryOrderer.BuildEffectiveOrders([Order("__name__", Asc)], null);

        // __name__ is already present; it should not be added again
        Assert.Single(effective);
        Assert.Equal("__name__", effective[0].Field.FieldPath);
    }

    // ── Multi-field sort ──────────────────────────────────────────────────

    [Fact]
    public void Sort_MultiField_SortsCorrectly()
    {
        var docs = new[]
        {
            DocWithTwoFields("category", Str("B"), "score", Int(20), "b-20"),
            DocWithTwoFields("category", Str("A"), "score", Int(30), "a-30"),
            DocWithTwoFields("category", Str("A"), "score", Int(10), "a-10"),
        };

        var result = QueryOrderer.Sort(docs,
            [Order("category", Asc), Order("score", Asc)], null);

        Assert.EndsWith("/a-10", result[0].Name, StringComparison.Ordinal);
        Assert.EndsWith("/a-30", result[1].Name, StringComparison.Ordinal);
        Assert.EndsWith("/b-20", result[2].Name, StringComparison.Ordinal);
    }

    // ── Missing fields sort before present fields ──────────────────────────

    [Fact]
    public void Sort_MissingField_SortsLikeNull()
    {
        // Missing fields produce null from GetSortValue → sorted as null (first in Firestore ordering)
        var docs = new[]
        {
            DocWithField("score", Int(5), "with-score"),
            NamedDoc("no-score"),
        };

        var result = QueryOrderer.Sort(docs, [Order("score", Asc)], null);

        // null < 5 in Firestore ordering, but with implicit __name__ tiebreak,
        // the document without the field should come first
        Assert.EndsWith("/no-score", result[0].Name, StringComparison.Ordinal);
    }

    // ── BuildEffectiveOrders ──────────────────────────────────────────────

    [Fact]
    public void BuildEffectiveOrders_EmptyOrderBy_AddsNameAscending()
    {
        var effective = QueryOrderer.BuildEffectiveOrders([], null);

        Assert.Single(effective);
        Assert.Equal("__name__", effective[0].Field.FieldPath);
        Assert.Equal(StructuredQuery.Types.Direction.Ascending, effective[0].Direction);
    }

    [Fact]
    public void BuildEffectiveOrders_DescendingOrder_AddsNameDescending()
    {
        var effective = QueryOrderer.BuildEffectiveOrders([Order("score", Desc)], null);

        Assert.Equal(2, effective.Count);
        Assert.Equal("__name__", effective[1].Field.FieldPath);
        Assert.Equal(StructuredQuery.Types.Direction.Descending, effective[1].Direction);
    }

    // ── Factories ─────────────────────────────────────────────────────────

    private static readonly StructuredQuery.Types.Direction Asc =
        StructuredQuery.Types.Direction.Ascending;
    private static readonly StructuredQuery.Types.Direction Desc =
        StructuredQuery.Types.Direction.Descending;

    private static StructuredQuery.Types.Order Order(string field, StructuredQuery.Types.Direction dir) =>
        new()
        {
            Field = new StructuredQuery.Types.FieldReference { FieldPath = field },
            Direction = dir
        };

    private static Value Int(long v) => new() { IntegerValue = v };
    private static Value Str(string v) => new() { StringValue = v };

    private static Document NamedDoc(string docId)
    {
        var doc = new Document { Name = $"{Parent}/col/{docId}" };
        return doc;
    }

    private static Document DocWithField(string field, Value value, string docId)
    {
        var doc = new Document { Name = $"{Parent}/col/{docId}" };
        var path = FirestoreSharp.Core.FieldPath.Parse(field);
        FirestoreSharp.Core.DocumentNavigator.SetValue(doc, path, value);
        return doc;
    }

    private static Document DocWithTwoFields(string f1, Value v1, string f2, Value v2, string docId)
    {
        var doc = DocWithField(f1, v1, docId);
        FirestoreSharp.Core.DocumentNavigator.SetValue(doc, FirestoreSharp.Core.FieldPath.Parse(f2), v2);
        return doc;
    }
}
