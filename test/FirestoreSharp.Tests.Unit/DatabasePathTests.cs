using FirestoreSharp.Core;
using Xunit;

namespace FirestoreSharp.Tests.Unit;

public sealed class DatabasePathTests
{
    // ── Parse — valid inputs ──────────────────────────────────────────────────

    [Fact]
    public void Parse_ExtractsProjectAndDatabase()
    {
        var path = DatabasePath.Parse("projects/my-proj/databases/(default)");

        Assert.Equal("my-proj", path.Project);
        Assert.Equal("(default)", path.Database);
    }

    [Fact]
    public void Parse_CustomDatabase_ExtractsComponents()
    {
        var path = DatabasePath.Parse("projects/p1/databases/my-db");

        Assert.Equal("p1", path.Project);
        Assert.Equal("my-db", path.Database);
    }

    [Fact]
    public void ResourceName_RoundTrips()
    {
        const string name = "projects/p1/databases/(default)";
        var path = DatabasePath.Parse(name);

        Assert.Equal(name, path.ResourceName.ToString());
    }

    [Fact]
    public void ToString_ReturnsResourceName()
    {
        const string name = "projects/p1/databases/(default)";
        var path = DatabasePath.Parse(name);

        Assert.Equal(name, path.ToString());
    }

    [Fact]
    public void DocumentsRoot_AppendsDocumentsSegment()
    {
        var path = DatabasePath.Parse("projects/p1/databases/(default)");

        Assert.Equal("projects/p1/databases/(default)/documents", path.DocumentsRoot);
    }

    // ── Parse — invalid inputs ────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("projects")]
    [InlineData("projects/p1")]
    [InlineData("projects/p1/databases")]
    [InlineData("projects//databases/(default)")]   // empty project
    [InlineData("projects/p1/databases/")]           // empty database
    public void Parse_InvalidPath_Throws(string input)
    {
        Assert.ThrowsAny<ArgumentException>(() => DatabasePath.Parse(input));
    }

    [Fact]
    public void Parse_WrongPrefix_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DatabasePath.Parse("buckets/b1/databases/db"));
    }

    // ── Equality ─────────────────────────────────────────────────────────────

    [Fact]
    public void Equals_SameResourceName_ReturnsTrue()
    {
        var a = DatabasePath.Parse("projects/p1/databases/(default)");
        var b = DatabasePath.Parse("projects/p1/databases/(default)");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_DifferentResourceName_ReturnsFalse()
    {
        var a = DatabasePath.Parse("projects/p1/databases/(default)");
        var b = DatabasePath.Parse("projects/p2/databases/(default)");

        Assert.NotEqual(a, b);
    }

    // ── IsDatabaseRoot ────────────────────────────────────────────────────────

    [Fact]
    public void IsDatabaseRoot_DatabaseRootPath_ReturnsTrueWithParsedPath()
    {
        var result = DatabasePath.IsDatabaseRoot("projects/p1/databases/(default)/documents", out var db);

        Assert.True(result);
        Assert.Equal("p1", db.Project);
        Assert.Equal("(default)", db.Database);
    }

    [Fact]
    public void IsDatabaseRoot_CollectionPath_ReturnsFalse()
    {
        Assert.False(DatabasePath.IsDatabaseRoot("projects/p1/databases/(default)/documents/users", out _));
    }

    [Fact]
    public void IsDatabaseRoot_DocumentPath_ReturnsFalse()
    {
        Assert.False(DatabasePath.IsDatabaseRoot("projects/p1/databases/(default)/documents/users/u1", out _));
    }

    [Fact]
    public void IsDatabaseRoot_MalformedPath_ReturnsFalse()
    {
        Assert.False(DatabasePath.IsDatabaseRoot("not/a/valid/path/documents", out _));
    }

    // ── CollectionPath integration ────────────────────────────────────────────

    [Fact]
    public void CollectionPath_ExposesDatabase()
    {
        var collection = CollectionPath.Parse("projects/p1/databases/(default)/documents/users");

        Assert.Equal("p1", collection.DatabasePath.Project);
        Assert.Equal("(default)", collection.DatabasePath.Database);
        Assert.Equal("projects/p1/databases/(default)", collection.DatabasePath.ResourceName.ToString());
    }
}
