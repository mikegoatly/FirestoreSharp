using FirestoreSharp.Core.Query;
using FirestoreSharp.Tests.Unit.Builders;
using Google.Cloud.Firestore.V1;
using Xunit;

using Value = Google.Cloud.Firestore.V1.Value;
using FieldOp = Google.Cloud.Firestore.V1.StructuredQuery.Types.FieldFilter.Types.Operator;

namespace FirestoreSharp.Tests.Unit.Query;

public sealed class QueryEngineTests
{
    private const string Parent = "projects/test-project/databases/(default)/documents";
    private const string Database = "projects/test-project/databases/(default)";

    // ── Collection resolution (from) ──────────────────────────────────────

    [Fact]
    public void Execute_FromCollection_ReturnsOnlyDirectChildren()
    {
        var docs = new[]
        {
            Named($"{Parent}/users/alice"),
            Named($"{Parent}/users/bob"),
            Named($"{Parent}/orders/o1"),
            Named($"{Parent}/users/alice/posts/p1"), // subcollection — should be excluded
        };

        var query = new StructuredQuery();
        query.From.Add(CollectionSelector("users", allDescendants: false));

        var result = QueryEngine.Execute(Parent, query, docs);

        Assert.Equal(2, result.Count);
        Assert.All(result, d => Assert.Matches(@"/users/[^/]+$", d.Name));
    }

    [Fact]
    public void Execute_AllDescendants_ReturnsAnyNestedCollection()
    {
        var docs = new[]
        {
            Named($"{Parent}/users/alice"),                      // direct child
            Named($"{Parent}/users/alice/posts/p1"),             // nested — should be included
            Named($"{Parent}/orders/o1"),                        // different collection — excluded
        };

        var query = new StructuredQuery();
        query.From.Add(CollectionSelector("users", allDescendants: true));

        var result = QueryEngine.Execute(Parent, query, docs);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, d => d.Name.EndsWith("/users/alice", StringComparison.Ordinal));
        Assert.Contains(result, d => d.Name.EndsWith("/posts/p1", StringComparison.Ordinal));
    }

    // ── Filtering (where) ─────────────────────────────────────────────────

    [Fact]
    public void Execute_EqualityFilter_ReturnsMatchingDocuments()
    {
        var docs = new[]
        {
            DocInUsers("alice", ("status", Str("active"))),
            DocInUsers("bob", ("status", Str("inactive"))),
            DocInUsers("charlie", ("status", Str("active"))),
        };

        var query = QueryWithFilter("users",
            FieldFilter("status", FieldOp.Equal, Str("active")));

        var result = QueryEngine.Execute(Parent, query, docs);

        Assert.Equal(2, result.Count);
        Assert.All(result, d => Assert.Equal("active", d.Fields["status"].StringValue));
    }

    [Fact]
    public void Execute_InequalityFilter_ReturnsMatchingDocuments()
    {
        var docs = new[]
        {
            DocInUsers("u1", ("age", Int(15))),
            DocInUsers("u2", ("age", Int(20))),
            DocInUsers("u3", ("age", Int(25))),
        };

        var query = QueryWithFilter("users",
            FieldFilter("age", FieldOp.GreaterThanOrEqual, Int(20)));

        var result = QueryEngine.Execute(Parent, query, docs);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Execute_NoMatchingDocuments_ReturnsEmpty()
    {
        var docs = new[]
        {
            DocInUsers("u1", ("status", Str("inactive"))),
        };

        var query = QueryWithFilter("users",
            FieldFilter("status", FieldOp.Equal, Str("active")));

        var result = QueryEngine.Execute(Parent, query, docs);

        Assert.Empty(result);
    }

    // ── Ordering ──────────────────────────────────────────────────────────

    [Fact]
    public void Execute_OrderBy_SortsResults()
    {
        var docs = new[]
        {
            DocInUsers("u3", ("score", Int(30))),
            DocInUsers("u1", ("score", Int(10))),
            DocInUsers("u2", ("score", Int(20))),
        };

        var query = new StructuredQuery();
        query.From.Add(CollectionSelector("users", allDescendants: false));
        query.OrderBy.Add(Order("score", Asc));

        var result = QueryEngine.Execute(Parent, query, docs);

        Assert.Equal(10L, result[0].Fields["score"].IntegerValue);
        Assert.Equal(20L, result[1].Fields["score"].IntegerValue);
        Assert.Equal(30L, result[2].Fields["score"].IntegerValue);
    }

    // ── Offset + limit ────────────────────────────────────────────────────

    [Fact]
    public void Execute_Limit_CapResults()
    {
        var docs = Enumerable.Range(1, 10)
            .Select(i => DocInUsers($"u{i:D2}", ("score", Int(i))))
            .ToArray();

        var query = new StructuredQuery();
        query.From.Add(CollectionSelector("users", allDescendants: false));
        query.OrderBy.Add(Order("score", Asc));
        query.Limit = 3;

        var result = QueryEngine.Execute(Parent, query, docs);

        Assert.Equal(3, result.Count);
        Assert.Equal(1L, result[0].Fields["score"].IntegerValue);
    }

    [Fact]
    public void Execute_Offset_SkipsResults()
    {
        var docs = Enumerable.Range(1, 5)
            .Select(i => DocInUsers($"u{i:D2}", ("score", Int(i))))
            .ToArray();

        var query = new StructuredQuery();
        query.From.Add(CollectionSelector("users", allDescendants: false));
        query.OrderBy.Add(Order("score", Asc));
        query.Offset = 2;

        var result = QueryEngine.Execute(Parent, query, docs);

        Assert.Equal(3, result.Count);
        Assert.Equal(3L, result[0].Fields["score"].IntegerValue);
    }

    [Fact]
    public void Execute_OffsetAndLimit_PagesResults()
    {
        var docs = Enumerable.Range(1, 10)
            .Select(i => DocInUsers($"u{i:D2}", ("score", Int(i))))
            .ToArray();

        var query = new StructuredQuery();
        query.From.Add(CollectionSelector("users", allDescendants: false));
        query.OrderBy.Add(Order("score", Asc));
        query.Offset = 3;
        query.Limit = 4;

        var result = QueryEngine.Execute(Parent, query, docs);

        Assert.Equal(4, result.Count);
        Assert.Equal(4L, result[0].Fields["score"].IntegerValue);
        Assert.Equal(7L, result[^1].Fields["score"].IntegerValue);
    }

    // ── Projection (select) ───────────────────────────────────────────────

    [Fact]
    public void Execute_Select_ReturnsOnlySpecifiedFields()
    {
        var docs = new[]
        {
            DocInUsers("alice",
                ("name", Str("Alice")),
                ("email", Str("alice@example.com")),
                ("age", Int(30)))
        };

        var query = new StructuredQuery();
        query.From.Add(CollectionSelector("users", allDescendants: false));
        query.Select = new StructuredQuery.Types.Projection();
        query.Select.Fields.Add(new StructuredQuery.Types.FieldReference { FieldPath = "name" });
        query.Select.Fields.Add(new StructuredQuery.Types.FieldReference { FieldPath = "email" });

        var result = QueryEngine.Execute(Parent, query, docs);

        var doc = Assert.Single(result);
        Assert.True(doc.Fields.ContainsKey("name"));
        Assert.True(doc.Fields.ContainsKey("email"));
        Assert.False(doc.Fields.ContainsKey("age"));
    }

    [Fact]
    public void Execute_EmptySelect_ReturnsAllFields()
    {
        var docs = new[]
        {
            DocInUsers("alice",
                ("name", Str("Alice")),
                ("email", Str("alice@example.com")))
        };

        var query = new StructuredQuery();
        query.From.Add(CollectionSelector("users", allDescendants: false));
        // No select set — should return all fields

        var result = QueryEngine.Execute(Parent, query, docs);

        var doc = Assert.Single(result);
        Assert.True(doc.Fields.ContainsKey("name"));
        Assert.True(doc.Fields.ContainsKey("email"));
    }

    // ── Combined pipeline ─────────────────────────────────────────────────

    [Fact]
    public void Execute_FilterOrderLimit_CombinedCorrectly()
    {
        var docs = new[]
        {
            DocInUsers("u1", ("active", Bool(true)),  ("score", Int(10))),
            DocInUsers("u2", ("active", Bool(false)), ("score", Int(50))),
            DocInUsers("u3", ("active", Bool(true)),  ("score", Int(30))),
            DocInUsers("u4", ("active", Bool(true)),  ("score", Int(20))),
        };

        var query = new StructuredQuery();
        query.From.Add(CollectionSelector("users", allDescendants: false));
        query.Where = FieldFilter("active", FieldOp.Equal, new Value { BooleanValue = true });
        query.OrderBy.Add(Order("score", Asc));
        query.Limit = 2;

        var result = QueryEngine.Execute(Parent, query, docs);

        Assert.Equal(2, result.Count);
        Assert.Equal(10L, result[0].Fields["score"].IntegerValue);
        Assert.Equal(20L, result[1].Fields["score"].IntegerValue);
    }

    // ── Factories ─────────────────────────────────────────────────────────

    private static readonly StructuredQuery.Types.Direction Asc =
        StructuredQuery.Types.Direction.Ascending;

    private static Document Named(string name) => new() { Name = name };

    private static Document DocInUsers(string docId, params (string field, Value value)[] fields)
    {
        var doc = new Document { Name = $"{Parent}/users/{docId}" };
        foreach (var (field, value) in fields)
        {
            FirestoreSharp.Core.DocumentNavigator.SetValue(
                doc, FirestoreSharp.Core.FieldPath.Parse(field), value);
        }
        return doc;
    }

    private static Value Str(string v) => new() { StringValue = v };
    private static Value Int(long v) => new() { IntegerValue = v };
    private static Value Bool(bool v) => new() { BooleanValue = v };

    private static StructuredQuery.Types.CollectionSelector CollectionSelector(string id, bool allDescendants) =>
        new() { CollectionId = id, AllDescendants = allDescendants };

    private static StructuredQuery.Types.Order Order(string field, StructuredQuery.Types.Direction dir) =>
        new()
        {
            Field = new StructuredQuery.Types.FieldReference { FieldPath = field },
            Direction = dir
        };

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

    private static StructuredQuery QueryWithFilter(string collectionId, StructuredQuery.Types.Filter filter)
    {
        var query = new StructuredQuery();
        query.From.Add(CollectionSelector(collectionId, allDescendants: false));
        query.Where = filter;
        return query;
    }
}
