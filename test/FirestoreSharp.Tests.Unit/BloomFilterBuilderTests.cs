using FirestoreSharp.Core.Listeners;

using Google.Cloud.Firestore.V1;
using Google.Protobuf;

using Xunit;

namespace FirestoreSharp.Tests.Unit;

/// <summary>
/// Tests using golden test vectors from Google's server-side test suite, shared across all
/// Firebase SDKs (JS, Android, iOS):
/// https://github.com/firebase/firebase-js-sdk/tree/main/packages/firestore/test/unit/remote/bloom_filter_golden_test_data
/// </summary>
public sealed class BloomFilterBuilderTests
{
    // ── MightContain: golden test vectors ────────────────────────────────────

    /// <summary>
    /// 1 document, FPR=0.0001.
    /// bitmap=RswZ (base64) → [0x46,0xCC,0x19], padding=1, hashCount=16.
    /// membershipTestResults: doc0=true, doc1=false.
    /// </summary>
    [Theory]
    [InlineData("projects/project-1/databases/database-1/documents/coll/doc0", true)]
    [InlineData("projects/project-1/databases/database-1/documents/coll/doc1", false)]
    public void MightContain_Golden_1Doc_Fpr0001(string name, bool expected)
    {
        var filter = MakeFilter(bitmap: [0x46, 0xCC, 0x19], padding: 1, hashCount: 16);
        Assert.Equal(expected, BloomFilterBuilder.MightContain(filter, name));
    }

    /// <summary>
    /// 1 document, FPR=0.01.
    /// bitmap=mwE= (base64) → [0x9B,0x01], padding=5, hashCount=8.
    /// membershipTestResults: doc0=true, doc1=false.
    /// </summary>
    [Theory]
    [InlineData("projects/project-1/databases/database-1/documents/coll/doc0", true)]
    [InlineData("projects/project-1/databases/database-1/documents/coll/doc1", false)]
    public void MightContain_Golden_1Doc_Fpr001(string name, bool expected)
    {
        var filter = MakeFilter(bitmap: [0x9B, 0x01], padding: 5, hashCount: 8);
        Assert.Equal(expected, BloomFilterBuilder.MightContain(filter, name));
    }

    /// <summary>
    /// FPR=1.0 edge case: empty filter (zero bits, zero hashes) → always returns false.
    /// </summary>
    [Theory]
    [InlineData("projects/project-1/databases/database-1/documents/coll/doc0")]
    [InlineData("projects/project-1/databases/database-1/documents/coll/doc1")]
    public void MightContain_EmptyFilter_AlwaysReturnsFalse(string name)
    {
        var filter = MakeFilter(bitmap: [], padding: 0, hashCount: 0);
        Assert.False(BloomFilterBuilder.MightContain(filter, name));
    }

    /// <summary>
    /// Unicode document name test vectors from the golden suite.
    /// bitmap=[0xED,0x05], padding=5, hashCount=8, bitCount=11.
    /// "ÀÒ∑" (U+00C0 U+00D2 U+2211) was inserted → true.
    /// "Ò∑À" was not inserted → false.
    /// </summary>
    [Theory]
    [InlineData("ÀÒ∑", true)]
    [InlineData("Ò∑À", false)]
    public void MightContain_Golden_UnicodeNames(string name, bool expected)
    {
        var filter = MakeFilter(bitmap: [0xED, 0x05], padding: 5, hashCount: 8);
        Assert.Equal(expected, BloomFilterBuilder.MightContain(filter, name));
    }

    /// <summary>
    /// Empty string with single-bit filter set to the bit the empty string hashes to → true.
    /// bitmap=[0xFF], padding=0, hashCount=16 → all 8 bits set → any single hash lands here.
    /// </summary>
    [Fact]
    public void MightContain_EmptyString_AllBitsSet_ReturnsTrue()
    {
        var filter = MakeFilter(bitmap: [0xFF], padding: 0, hashCount: 16);
        Assert.True(BloomFilterBuilder.MightContain(filter, ""));
    }

    /// <summary>
    /// Empty string with only bit 0 set, hashCount=1.
    /// The empty string MD5 → h1=0xD98C1DD404B2008F, h2=0x9800998ECE08761F.
    /// h(0) = h1 % 7 = ... does not land on bit 0 of [0x01], so false.
    /// </summary>
    [Fact]
    public void MightContain_EmptyString_OnlyBit0Set_ReturnsFalse()
    {
        var filter = MakeFilter(bitmap: [0x01], padding: 1, hashCount: 1);
        Assert.False(BloomFilterBuilder.MightContain(filter, ""));
    }

    // ── Build → MightContain round-trip ──────────────────────────────────────

    [Fact]
    public void Build_SingleDocument_CanBeFound()
    {
        var name = "projects/p/databases/d/documents/col/doc1";
        var filter = BloomFilterBuilder.Build([name]);

        Assert.NotNull(filter);
        Assert.True(BloomFilterBuilder.MightContain(filter, name));
    }

    [Fact]
    public void Build_ManyDocuments_AllCanBeFound()
    {
        var names = Enumerable.Range(0, 100)
            .Select(i => $"projects/p/databases/d/documents/col/doc{i}")
            .ToList();

        var filter = BloomFilterBuilder.Build(names);

        Assert.NotNull(filter);
        foreach (var name in names)
        {
            Assert.True(BloomFilterBuilder.MightContain(filter, name),
                $"Expected bloom filter to contain '{name}'");
        }
    }

    [Fact]
    public void Build_EmptySet_ReturnsNull()
    {
        Assert.Null(BloomFilterBuilder.Build([]));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BloomFilter MakeFilter(byte[] bitmap, int padding, int hashCount) =>
        new()
        {
            Bits = new BitSequence
            {
                Bitmap = ByteString.CopyFrom(bitmap),
                Padding = padding,
            },
            HashCount = hashCount,
        };
}
