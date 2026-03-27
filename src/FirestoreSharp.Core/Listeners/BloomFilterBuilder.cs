using System.Security.Cryptography;
using System.Text;

using Google.Cloud.Firestore.V1;
using Google.Protobuf;

namespace FirestoreSharp.Core.Listeners;

/// <summary>
/// Builds a Firestore-compatible BloomFilter from a set of document resource names.
/// Hash algorithm per proto spec: MD5 of UTF-8 name → two 64-bit unsigned ints (h1=low, h2=high)
/// → h(i) = h1 + i*h2, taken mod num_bits.
/// </summary>
internal static class BloomFilterBuilder
{
    // Target false-positive rate for sizing.
    private const double FalsePositiveRate = 0.001;

    /// <summary>
    /// Builds a <see cref="BloomFilter"/> containing all <paramref name="documentNames"/>.
    /// Returns <c>null</c> when the set is empty (no filter needed).
    /// </summary>
    public static BloomFilter? Build(IReadOnlyCollection<string> documentNames)
    {
        var n = documentNames.Count;
        if (n == 0)
        {
            return null;
        }

        var (numBits, hashCount) = ComputeParameters(n);

        var byteCount = (numBits + 7) / 8;
        var bitmap = new byte[byteCount];

        foreach (var name in documentNames)
        {
            var (h1, h2) = ComputeHashes(name);
            for (var i = 0; i < hashCount; i++)
            {
                var bitIndex = (long)((h1 + (ulong)i * h2) % (ulong)numBits);
                bitmap[bitIndex / 8] |= (byte)(1 << (int)(bitIndex % 8));
            }
        }

        var padding = (byteCount * 8) - numBits;

        return new BloomFilter
        {
            Bits = new BitSequence
            {
                Bitmap = ByteString.CopyFrom(bitmap),
                Padding = padding,
            },
            HashCount = hashCount,
        };
    }

    /// <summary>
    /// Tests whether <paramref name="name"/> might be in the given <paramref name="filter"/>.
    /// Returns <c>false</c> only when the name is definitely absent; <c>true</c> means probably present.
    /// </summary>
    public static bool MightContain(BloomFilter filter, string name)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(filter.Bits);

        var bitmap = filter.Bits.Bitmap.ToByteArray();
        var numBits = (bitmap.Length * 8) - filter.Bits.Padding;
        if (numBits <= 0) return false;

        var (h1, h2) = ComputeHashes(name);
        for (var i = 0; i < filter.HashCount; i++)
        {
            var bitIndex = (long)((h1 + (ulong)i * h2) % (ulong)numBits);
            if ((bitmap[bitIndex / 8] & (1 << (int)(bitIndex % 8))) == 0)
            {
                return false;
            }
        }

        return true;
    }

    // ── Parameters ────────────────────────────────────────────────────────────

    private static (int numBits, int hashCount) ComputeParameters(int n)
    {
        // Optimal bit count: m = -n * ln(p) / ln(2)^2
        var ln2Squared = Math.Log(2) * Math.Log(2);
        var numBits = (int)Math.Ceiling(-n * Math.Log(FalsePositiveRate) / ln2Squared);
        if (numBits < 1) numBits = 1;

        // Optimal hash count: k = m/n * ln(2)
        var hashCount = (int)Math.Ceiling((double)numBits / n * Math.Log(2));
        if (hashCount < 1) hashCount = 1;

        return (numBits, hashCount);
    }

    // ── Hashing ───────────────────────────────────────────────────────────────

    private static (ulong h1, ulong h2) ComputeHashes(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
#pragma warning disable CA5351 // MD5 is required by the Firestore BloomFilter proto spec — not used for security.
        var hash = MD5.HashData(bytes); // 16 bytes
#pragma warning restore CA5351

        // Interpret as two little-endian 64-bit unsigned ints.
        var h1 = BitConverter.ToUInt64(hash, 0);
        var h2 = BitConverter.ToUInt64(hash, 8);
        return (h1, h2);
    }
}
