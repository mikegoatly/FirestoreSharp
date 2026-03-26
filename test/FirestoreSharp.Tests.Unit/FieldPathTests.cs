using FirestoreSharp.Core;
using Xunit;

namespace FirestoreSharp.Tests.Unit;

public sealed class FieldPathTests
{
    [Fact]
    public void Parse_SimpleSegment_ReturnsSingleSegment()
    {
        var path = FieldPath.Parse("name");

        Assert.Single(path.Segments);
        Assert.Equal("name", path.Segments[0]);
    }

    [Fact]
    public void Parse_DottedPath_ReturnsTwoSegments()
    {
        var path = FieldPath.Parse("address.city");

        Assert.Equal(2, path.Segments.Count);
        Assert.Equal("address", path.Segments[0]);
        Assert.Equal("city", path.Segments[1]);
    }

    [Fact]
    public void Parse_DeeplyNested_ReturnsAllSegments()
    {
        var path = FieldPath.Parse("a.b.c.d");

        Assert.Equal(4, path.Segments.Count);
        Assert.Equal(["a", "b", "c", "d"], path.Segments);
    }

    [Fact]
    public void Parse_QuotedSegment_StripsBackticks()
    {
        var path = FieldPath.Parse("`x&y`");

        Assert.Single(path.Segments);
        Assert.Equal("x&y", path.Segments[0]);
    }

    [Fact]
    public void Parse_MixedSimpleAndQuoted_ParsesBoth()
    {
        var path = FieldPath.Parse("foo.`x&y`");

        Assert.Equal(2, path.Segments.Count);
        Assert.Equal("foo", path.Segments[0]);
        Assert.Equal("x&y", path.Segments[1]);
    }

    [Fact]
    public void Parse_EscapedBacktickInQuotedSegment_Unescapes()
    {
        // `bak\`tik` → bak`tik
        var path = FieldPath.Parse(@"`bak\`tik`");

        Assert.Single(path.Segments);
        Assert.Equal("bak`tik", path.Segments[0]);
    }

    [Fact]
    public void Parse_EscapedBackslashInQuotedSegment_Unescapes()
    {
        // `a\\b` → a\b
        var path = FieldPath.Parse(@"`a\\b`");

        Assert.Single(path.Segments);
        Assert.Equal(@"a\b", path.Segments[0]);
    }

    [Fact]
    public void Parse_UnderscorePrefix_IsValid()
    {
        var path = FieldPath.Parse("_private.field_name");

        Assert.Equal(2, path.Segments.Count);
        Assert.Equal("_private", path.Segments[0]);
        Assert.Equal("field_name", path.Segments[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Parse_EmptyOrWhitespace_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => FieldPath.Parse(input));
    }

    [Fact]
    public void Parse_TrailingDot_Throws()
    {
        Assert.Throws<ArgumentException>(() => FieldPath.Parse("foo."));
    }

    [Fact]
    public void Parse_UnterminatedQuote_Throws()
    {
        Assert.Throws<ArgumentException>(() => FieldPath.Parse("`unterminated"));
    }

    [Fact]
    public void ToString_SimpleSegments_JoinsWithDots()
    {
        var path = FieldPath.FromSegments("a", "b", "c");

        Assert.Equal("a.b.c", path.ToString());
    }

    [Fact]
    public void ToString_SpecialCharSegment_QuotesWithBackticks()
    {
        var path = FieldPath.FromSegments("foo", "x&y");

        Assert.Equal("foo.`x&y`", path.ToString());
    }

    [Fact]
    public void ToString_SegmentWithBacktick_EscapesBacktick()
    {
        var path = FieldPath.FromSegments("bak`tik");

        Assert.Equal(@"`bak\`tik`", path.ToString());
    }

    [Fact]
    public void RoundTrip_ComplexPath_PreservesSegments()
    {
        var original = FieldPath.FromSegments("foo", "x&y", "bak`tik", "simple");
        var roundTripped = FieldPath.Parse(original.ToString());

        Assert.Equal(original.Segments, roundTripped.Segments);
    }

    [Fact]
    public void FromSegments_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => FieldPath.FromSegments());
    }

    [Fact]
    public void FromSegments_EmptySegment_Throws()
    {
        Assert.Throws<ArgumentException>(() => FieldPath.FromSegments("a", "", "b"));
    }
}
