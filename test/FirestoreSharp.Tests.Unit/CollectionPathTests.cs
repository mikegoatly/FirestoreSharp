using FirestoreSharp.Core;
using Xunit;

namespace FirestoreSharp.Tests.Unit;

public sealed class CollectionPathTests
{
    // ── Parse — valid inputs ──────────────────────────────────────────────────

    [Fact]
    public void Parse_TopLevelCollection_ExtractsComponents()
    {
        var path = CollectionPath.Parse("projects/my-proj/databases/(default)/documents/users");

        Assert.Equal("my-proj", path.Project);
        Assert.Equal("(default)", path.Database);
        Assert.Equal(["users"], path.Segments);
    }

    [Fact]
    public void Parse_SubCollection_ExtractsComponents()
    {
        var path = CollectionPath.Parse("projects/p1/databases/db1/documents/users/u1/posts");

        Assert.Equal("p1", path.Project);
        Assert.Equal("db1", path.Database);
        Assert.Equal(["users", "u1", "posts"], path.Segments);
    }

    [Fact]
    public void Parse_DeeplyNestedCollection_ExtractsSegments()
    {
        var path = CollectionPath.Parse("projects/p/databases/d/documents/a/b/c/d/e");

        Assert.Equal(["a", "b", "c", "d", "e"], path.Segments);
    }

    [Fact]
    public void ResourceName_RoundTrips()
    {
        const string name = "projects/p1/databases/(default)/documents/users/u1/posts";
        var path = CollectionPath.Parse(name);

        Assert.Equal(name, path.ResourceName.ToString());
    }

    [Fact]
    public void ToString_ReturnsResourceName()
    {
        const string name = "projects/p1/databases/(default)/documents/users";
        var path = CollectionPath.Parse(name);

        Assert.Equal(name, path.ToString());
    }

    // ── Parse — invalid inputs ────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("projects")]
    [InlineData("projects/p1")]
    [InlineData("projects/p1/databases")]
    [InlineData("projects/p1/databases/db")]
    [InlineData("projects/p1/databases/db/documents")]              // 0 segments after documents
    [InlineData("projects/p1/databases/db/documents/col/doc")]      // even — ends on doc ID, not collection
    [InlineData("projects/p1/databases/db/documents/col/doc/sub/doc2")] // even — ends on doc ID
    public void Parse_InvalidPath_Throws(string input)
    {
        Assert.ThrowsAny<ArgumentException>(() => CollectionPath.Parse(input));
    }

    [Fact]
    public void Parse_WrongPrefix_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CollectionPath.Parse("buckets/b1/databases/db/documents/col"));
    }

    // ── IsDirectChildDocument ────────────────────────────────────────────────

    [Fact]
    public void IsDirectChildDocument_DirectChild_ReturnsTrue()
    {
        var collection = CollectionPath.Parse("projects/p/databases/d/documents/users");

        Assert.True(collection.IsDirectChildDocument("projects/p/databases/d/documents/users/alice"));
    }

    [Fact]
    public void IsDirectChildDocument_Grandchild_ReturnsFalse()
    {
        var collection = CollectionPath.Parse("projects/p/databases/d/documents/users");

        Assert.False(collection.IsDirectChildDocument("projects/p/databases/d/documents/users/alice/posts/post1"));
    }

    [Fact]
    public void IsDirectChildDocument_WrongCollection_ReturnsFalse()
    {
        var collection = CollectionPath.Parse("projects/p/databases/d/documents/users");

        Assert.False(collection.IsDirectChildDocument("projects/p/databases/d/documents/orders/o1"));
    }

    [Fact]
    public void IsDirectChildDocument_SubCollection_DirectChild_ReturnsTrue()
    {
        var collection = CollectionPath.Parse("projects/p/databases/d/documents/users/u1/posts");

        Assert.True(collection.IsDirectChildDocument("projects/p/databases/d/documents/users/u1/posts/post1"));
    }

    [Fact]
    public void IsDirectChildDocument_SubCollection_TooDeep_ReturnsFalse()
    {
        var collection = CollectionPath.Parse("projects/p/databases/d/documents/users/u1/posts");

        Assert.False(collection.IsDirectChildDocument("projects/p/databases/d/documents/users/u1/posts/post1/comments/c1"));
    }

    // ── HasCollectionAfter ───────────────────────────────────────────────────

    [Fact]
    public void HasCollectionAfter_ExactMatch_AtOffset_ReturnsTrue()
    {
        // Segments: ["users", "u1", "posts"]
        var collection = CollectionPath.Parse("projects/p/databases/d/documents/users/u1/posts");

        Assert.True(collection.HasCollectionAfter(0, "users"));
        Assert.True(collection.HasCollectionAfter(0, "posts"));
        Assert.True(collection.HasCollectionAfter(2, "posts"));
    }

    [Fact]
    public void HasCollectionAfter_CollectionBeforeOffset_ReturnsFalse()
    {
        // Segments: ["users", "u1", "posts"]
        var collection = CollectionPath.Parse("projects/p/databases/d/documents/users/u1/posts");

        // "users" is at index 0, but we start at offset 2 — should not find it
        Assert.False(collection.HasCollectionAfter(2, "users"));
    }

    [Fact]
    public void HasCollectionAfter_NonexistentId_ReturnsFalse()
    {
        var collection = CollectionPath.Parse("projects/p/databases/d/documents/users/u1/posts");

        Assert.False(collection.HasCollectionAfter(0, "orders"));
    }

    [Fact]
    public void HasCollectionAfter_OffsetBeyondEnd_ReturnsFalse()
    {
        var collection = CollectionPath.Parse("projects/p/databases/d/documents/users");

        Assert.False(collection.HasCollectionAfter(10, "users"));
    }

    // ── ToStorageSegments ────────────────────────────────────────────────────

    [Fact]
    public void ToStorageSegments_TopLevel_ReturnsProjectDbAndCollection()
    {
        var path = CollectionPath.Parse("projects/p1/databases/(default)/documents/users");

        Assert.Equal(["p1", "(default)", "users"], path.ToStorageSegments().ToArray());
    }

    [Fact]
    public void ToStorageSegments_SubCollection_ReturnsAllSegments()
    {
        var path = CollectionPath.Parse("projects/p1/databases/db/documents/users/u1/posts");

        Assert.Equal(["p1", "db", "users", "u1", "posts"], path.ToStorageSegments().ToArray());
    }
}
