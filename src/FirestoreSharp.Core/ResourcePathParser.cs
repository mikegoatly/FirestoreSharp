namespace FirestoreSharp.Core;

/// <summary>
/// Shared span-based parsing primitives for Firestore resource path types.
/// </summary>
internal static class ResourcePathParser
{
    /// <summary>
    /// Advances <paramref name="remaining"/> past <paramref name="prefix"/>.
    /// Returns <c>false</c> (without advancing) if the prefix is not matched.
    /// </summary>
    public static bool TryConsume(ref ReadOnlySpan<char> remaining, string prefix)
    {
        if (!remaining.StartsWith(prefix.AsSpan(), StringComparison.Ordinal))
        {
            return false;
        }

        remaining = remaining[prefix.Length..];
        return true;
    }

    /// <summary>
    /// Reads a non-empty segment up to the next '/', advancing <paramref name="remaining"/> past both
    /// the segment and the slash. Throws if the segment is missing or empty.
    /// </summary>
    public static ReadOnlySpan<char> ReadSegment(ref ReadOnlySpan<char> remaining, ReadOnlyMemory<char> fullPath, string errorContext)
    {
        var slash = remaining.IndexOf('/');
        if (slash < 0)
        {
            ThrowFormat(fullPath, errorContext, "unexpected end of path");
        }

        var seg = remaining[..slash];
        if (seg.IsEmpty || seg.Trim().IsEmpty)
        {
            ThrowFormat(fullPath, errorContext, "empty or whitespace path segment");
        }

        remaining = remaining[(slash + 1)..];
        return seg;
    }

    /// <summary>
    /// Reads the remainder of <paramref name="remaining"/> as a non-empty segment (no slash expected).
    /// Throws if empty.
    /// </summary>
    public static ReadOnlySpan<char> ReadFinalSegment(ref ReadOnlySpan<char> remaining, ReadOnlyMemory<char> fullPath, string errorContext)
    {
        if (remaining.IsEmpty || remaining.Trim().IsEmpty)
        {
            ThrowFormat(fullPath, errorContext, "empty or whitespace final segment");
        }

        var seg = remaining;
        remaining = ReadOnlySpan<char>.Empty;
        return seg;
    }

    /// <summary>
    /// Counts slash-separated segments in <paramref name="span"/>, validating each is non-empty.
    /// Throws if any segment is empty or whitespace.
    /// </summary>
    public static int CountAndValidateSegments(ReadOnlySpan<char> span, ReadOnlyMemory<char> fullPath, string errorContext)
    {
        if (!TryCountAndValidateSegments(span, out var count))
        {
            ThrowFormat(fullPath, errorContext, "empty or whitespace segment");
        }

        return count;
    }

    /// <summary>
    /// Counts slash-separated segments in <paramref name="span"/>, validating each is non-empty.
    /// Returns <c>false</c> if any segment is empty or whitespace.
    /// </summary>
    public static bool TryCountAndValidateSegments(ReadOnlySpan<char> span, out int count)
    {
        count = 0;
        var segStart = 0;
        for (var i = 0; i <= span.Length; i++)
        {
            if (i == span.Length || span[i] == '/')
            {
                var seg = span[segStart..i];
                if (seg.IsEmpty || seg.Trim().IsEmpty)
                {
                    return false;
                }

                count++;
                segStart = i + 1;
            }
        }
        return true;
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    public static void ThrowFormat(ReadOnlyMemory<char> resourceName, string context, string reason) =>
        throw new ArgumentException($"Invalid {context} ({reason}): '{resourceName}'", nameof(resourceName));
}
