using FirestoreSharp.Core;
using Xunit;

namespace FirestoreSharp.Tests.Unit;

public sealed class DocumentPathTests
{
    [Fact]
    public void Parse_SimpleDocument_ExtractsComponents()
    {
        var path = DocumentPath.Parse("projects/my-proj/databases/(default)/documents/users/alice".AsMemory());

        Assert.Equal("my-proj", path.Project);
        Assert.Equal("(default)", path.Database);
        Assert.Equal(["users"], path.Collection.Segments);
        Assert.Equal("alice", path.DocumentId);
    }

    [Fact]
    public void Parse_SubCollection_ExtractsComponents()
    {
        var path = DocumentPath.Parse("projects/p1/databases/db1/documents/users/u1/posts/post1".AsMemory());

        Assert.Equal("p1", path.Project);
        Assert.Equal("db1", path.Database);
        Assert.Equal(["users", "u1", "posts"], path.Collection.Segments);
        Assert.Equal("post1", path.DocumentId);
    }

    [Fact]
    public void Parse_DeeplyNested_ExtractsComponents()
    {
        var path = DocumentPath.Parse("projects/p/databases/d/documents/a/b/c/d/e/f".AsMemory());

        Assert.Equal(["a", "b", "c", "d", "e"], path.Collection.Segments);
        Assert.Equal("f", path.DocumentId);
    }

    [Fact]
    public void ResourceName_RoundTrips()
    {
        const string name = "projects/p1/databases/(default)/documents/users/u1/posts/post1";
        var path = DocumentPath.Parse(name.AsMemory());

        Assert.Equal(name, path.ResourceName);
    }

    [Fact]
    public void ToString_ReturnsResourceName()
    {
        const string name = "projects/p1/databases/(default)/documents/users/alice";
        var path = DocumentPath.Parse(name.AsMemory());

        Assert.Equal(name, path.ToString());
    }

    [Fact]
    public void FromCreateRequest_BuildsCorrectPath()
    {
        var path = DocumentPath.FromCreateRequest(
            "projects/p1/databases/(default)/documents",
            "users",
            "alice");

        Assert.Equal("p1", path.Project);
        Assert.Equal("(default)", path.Database);
        Assert.Equal(["users"], path.Collection.Segments);
        Assert.Equal("alice", path.DocumentId);
        Assert.Equal("projects/p1/databases/(default)/documents/users/alice", path.ResourceName);
    }

    [Fact]
    public void FromCreateRequest_SubCollection_BuildsCorrectPath()
    {
        var path = DocumentPath.FromCreateRequest(
            "projects/p1/databases/(default)/documents/users/u1",
            "posts",
            "post1");

        Assert.Equal(["users", "u1", "posts"], path.Collection.Segments);
        Assert.Equal("post1", path.DocumentId);
    }

    [Fact]
    public void ToStorageSegments_ReturnsProjectDbAndDocPath()
    {
        var path = DocumentPath.Parse("projects/p1/databases/(default)/documents/users/u1/posts/post1".AsMemory());

        Assert.Equal(["p1", "(default)", "users", "u1", "posts", "post1"], path.ToStorageSegments());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("projects")]
    [InlineData("projects/p1")]
    [InlineData("projects/p1/databases")]
    [InlineData("projects/p1/databases/db")]
    [InlineData("projects/p1/databases/db/documents")]
    [InlineData("projects/p1/databases/db/documents/col")]  // odd doc path — no document ID
    public void Parse_InvalidPath_Throws(string input)
    {
        Assert.ThrowsAny<ArgumentException>(() => DocumentPath.Parse(input.AsMemory()));
    }

    [Fact]
    public void Parse_WrongPrefix_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DocumentPath.Parse("buckets/b1/databases/db/documents/col/doc".AsMemory()));
    }

    [Fact]
    public void Parse_OddDocumentPath_Throws()
    {
        // 3 segments after "documents" = collection/doc/orphanCollection — no doc ID for the subcollection
        Assert.Throws<ArgumentException>(() =>
            DocumentPath.Parse("projects/p/databases/d/documents/col/doc/subcol".AsMemory()));
    }
}
