using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core.Query;

/// <summary>
/// Computes partition cursors for a <see cref="PartitionQueryRequest"/> over an ordered
/// sequence of documents.
///
/// <para>
/// <paramref name="documents"/> must already be sorted by <c>__name__ ASC</c>.
/// <paramref name="partitionCount"/> is the desired number of split-point cursors
/// (K cursors divide the result set into K+1 sub-ranges).
/// </para>
/// </summary>
internal static class PartitionEngine
{
    /// <summary>
    /// Returns up to <c>min(partitionCount, documents.Count - 1)</c> evenly-spaced
    /// <see cref="Cursor"/> split points, with optional <paramref name="pageSize"/> /
    /// <paramref name="pageToken"/> pagination.
    /// </summary>
    public static PartitionQueryResult Execute(
        IReadOnlyList<Document> documents,
        long partitionCount,
        int pageSize,
        string? pageToken)
    {
        // K cursors define K+1 sub-ranges; can't have more cursors than gaps between docs.
        var maxCursors = (int)Math.Min(partitionCount, Math.Max(0, documents.Count - 1));

        if (maxCursors == 0)
        {
            return new PartitionQueryResult([], null);
        }

        // Evenly-spaced split points.
        // Split i (1-based) sits at index: round(i * N / (maxCursors + 1)), clamped to [1, N-1].
        var allCursors = new List<Cursor>(maxCursors);
        for (var i = 1; i <= maxCursors; i++)
        {
            var splitIndex = (int)Math.Round((double)i * documents.Count / (maxCursors + 1));
            splitIndex = Math.Clamp(splitIndex, 1, documents.Count - 1);

            allCursors.Add(new Cursor
            {
                Before = true,
                Values = { new Value { ReferenceValue = documents[splitIndex].Name } }
            });
        }

        // Pagination: page_token is the reference value of the last cursor on the previous page.
        var effectivePageSize = pageSize > 0 ? pageSize : allCursors.Count;
        var startIndex = 0;

        if (!string.IsNullOrEmpty(pageToken))
        {
            startIndex = allCursors.FindIndex(c =>
                string.Compare(c.Values[0].ReferenceValue, pageToken, StringComparison.Ordinal) > 0);

            if (startIndex < 0)
            {
                return new PartitionQueryResult([], null);
            }
        }

        var page = allCursors.Skip(startIndex).Take(effectivePageSize).ToList();
        var nextPageToken = (startIndex + page.Count < allCursors.Count)
            ? page[^1].Values[0].ReferenceValue
            : null;

        return new PartitionQueryResult(page, nextPageToken);
    }
}
