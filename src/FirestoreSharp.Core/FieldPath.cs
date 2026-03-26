using System.Text;

namespace FirestoreSharp.Core;

/// <summary>
/// A parsed Firestore field path. Segments are dot-delimited, with backtick quoting
/// for names that contain special characters.
/// <para>
/// Simple segment: <c>[a-zA-Z_][a-zA-Z0-9_]*</c><br/>
/// Quoted segment: <c>`...`</c> with <c>\</c> escaping for <c>`</c> and <c>\</c>.
/// </para>
/// </summary>
public sealed class FieldPath
{
    public IReadOnlyList<string> Segments { get; }

    private FieldPath(IReadOnlyList<string> segments)
    {
        Segments = segments;
    }

    /// <summary>
    /// Creates a <see cref="FieldPath"/> from pre-split segments (no parsing needed).
    /// </summary>
    public static FieldPath FromSegments(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Length == 0)
        {
            throw new ArgumentException("Field path must have at least one segment.", nameof(segments));
        }

        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment))
            {
                throw new ArgumentException("Field path segments must not be empty.", nameof(segments));
            }
        }

        return new FieldPath(segments);
    }

    /// <summary>
    /// Parses a dot-delimited field path string, handling backtick-quoted segments.
    /// </summary>
    public static FieldPath Parse(string fieldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);

        var memory = fieldPath.AsMemory();
        var segments = new List<string>();
        var position = 0;

        while (position < memory.Length)
        {
            if (memory.Span[position] == '`')
            {
                segments.Add(ParseQuotedSegment(memory, ref position));
            }
            else
            {
                segments.Add(ParseSimpleSegment(memory, ref position));
            }

            if (position < memory.Length)
            {
                if (memory.Span[position] != '.')
                {
                    throw new ArgumentException($"Expected '.' at position {position} in field path: '{fieldPath}'", nameof(fieldPath));
                }

                position++; // skip the dot

                if (position >= memory.Length)
                {
                    throw new ArgumentException($"Field path must not end with '.': '{fieldPath}'", nameof(fieldPath));
                }
            }
        }

        if (segments.Count == 0)
        {
            throw new ArgumentException($"Field path must have at least one segment: '{fieldPath}'", nameof(fieldPath));
        }

        return new FieldPath(segments);
    }

    private static string ParseSimpleSegment(ReadOnlyMemory<char> fieldPath, ref int position)
    {
        var start = position;
        var span = fieldPath.Span;

        while (position < span.Length && span[position] != '.')
        {
            position++;
        }

        if (position == start)
        {
            throw new ArgumentException($"Empty segment at position {position} in field path: '{fieldPath}'", nameof(fieldPath));
        }

        return fieldPath.Slice(start, position - start).ToString();
    }

    private static string ParseQuotedSegment(ReadOnlyMemory<char> fieldPath, ref int position)
    {
        position++; // skip opening backtick
        var start = position;
        var span = fieldPath.Span;
        var hasEscapes = false;

        // First pass: scan for end, check if escapes exist
        var scanPos = position;
        while (scanPos < span.Length)
        {
            if (span[scanPos] == '\\')
            {
                hasEscapes = true;
                if (scanPos + 1 >= span.Length)
                {
                    throw new ArgumentException($"Unexpected end of field path after escape character: '{fieldPath}'", nameof(fieldPath));
                }

                scanPos += 2;
            }
            else if (span[scanPos] == '`')
            {
                break;
            }
            else
            {
                scanPos++;
            }
        }

        if (scanPos >= span.Length)
        {
            throw new ArgumentException($"Unterminated quoted segment in field path: '{fieldPath}'", nameof(fieldPath));
        }

        if (!hasEscapes)
        {
            // No escapes — slice directly, one string allocation
            var result = fieldPath.Slice(start, scanPos - start).ToString();
            position = scanPos + 1; // skip closing backtick
            return result;
        }

        // Has escapes — need to unescape
        var sb = new StringBuilder(scanPos - start);
        while (position < span.Length)
        {
            var c = span[position];

            if (c == '\\')
            {
                position++;
                sb.Append(span[position]);
                position++;
            }
            else if (c == '`')
            {
                position++; // skip closing backtick
                return sb.ToString();
            }
            else
            {
                sb.Append(c);
                position++;
            }
        }

        throw new ArgumentException($"Unterminated quoted segment in field path: '{fieldPath}'", nameof(fieldPath));
    }

    public override string ToString()
    {
        return string.Join(".", Segments.Select(FormatSegment));
    }

    private static string FormatSegment(string segment)
    {
        if (IsSimpleSegment(segment))
        {
            return segment;
        }

        return '`' + segment.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("`", "\\`", StringComparison.Ordinal) + '`';
    }

    private static bool IsSimpleSegment(ReadOnlySpan<char> segment)
    {
        if (segment.Length == 0)
        {
            return false;
        }

        if (segment[0] is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_'))
        {
            return false;
        }

        for (var i = 1; i < segment.Length; i++)
        {
            if (segment[i] is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
