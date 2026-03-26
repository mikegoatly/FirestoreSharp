using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core.Query;

/// <summary>
/// Executes a <see cref="StructuredQuery"/> against a sequence of candidate documents,
/// applying the full Firestore query pipeline in order:
/// <list type="number">
/// <item>Collection resolution (from)</item>
/// <item>Filtering (where)</item>
/// <item>Ordering (order_by + implicit rules)</item>
/// <item>Offset + limit</item>
/// <item>Projection (select)</item>
/// </list>
/// </summary>
internal static class QueryEngine
{
    /// <summary>
    /// Runs <paramref name="query"/> against <paramref name="candidates"/>, where
    /// <paramref name="parent"/> is the resource path that was used as the query root
    /// (e.g. <c>projects/p/databases/d/documents</c> or a document path for subcollection queries).
    /// </summary>
    public static IReadOnlyList<Document> Execute(
        string parent,
        StructuredQuery query,
        IEnumerable<Document> candidates)
    {
        // 1. from — collection resolution
        var fromCollections = query.From;
        IEnumerable<Document> results = fromCollections.Count > 0
            ? candidates.Where(d => MatchesCollection(d, parent, fromCollections))
            : candidates;

        // 2. where — filtering
        if (query.Where is { FilterTypeCase: not StructuredQuery.Types.Filter.FilterTypeOneofCase.None })
        {
            results = results.Where(d => QueryFilter.Matches(d, query.Where));
        }

        // 3. order_by (includes implicit __name__ appending)
        var sorted = QueryOrderer.Sort(results, query.OrderBy, query.Where);

        // 4. offset + limit
        IEnumerable<Document> paged = sorted;
        if (query.Offset > 0)
        {
            paged = paged.Skip(query.Offset);
        }
        if (query.Limit is not null && query.Limit.Value > 0)
        {
            paged = paged.Take(query.Limit.Value);
        }

        // 5. select — projection
        var projection = query.Select?.Fields?.Count > 0 ? query.Select : null;
        return paged.Select(d => QueryProjection.Apply(d, projection)).ToList();
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="document"/> belongs to one of the collections
    /// described by <paramref name="selectors"/>.
    /// </summary>
    private static bool MatchesCollection(
        Document document,
        string parent,
        IEnumerable<StructuredQuery.Types.CollectionSelector> selectors)
    {
        return selectors.Any(selector => MatchesSelector(document, parent, selector));
    }

    private static bool MatchesSelector(
        Document document,
        string parent,
        StructuredQuery.Types.CollectionSelector selector)
    {
        var name = document.Name;

        if (selector.AllDescendants)
        {
            if (!name.StartsWith(parent + "/", StringComparison.Ordinal))
            {
                return false;
            }

            if (string.IsNullOrEmpty(selector.CollectionId))
            {
                return true;
            }

            // Segments in the parent after "documents" tells us where relative collections start.
            // DB root  (projects/p/databases/d/documents) → 0 parent segments
            // Doc path (projects/p/databases/d/documents/users/u1) → 2 parent segments
            var parentSegmentCount = Math.Max(0, parent.Split('/').Length - 5);
            var docPath = DocumentPath.Parse(name);
            return docPath.Collection.HasCollectionAfter(parentSegmentCount, selector.CollectionId);
        }
        else
        {
            if (string.IsNullOrEmpty(selector.CollectionId))
            {
                // Any direct child: parent/{anyCollection}/{docId} — exactly 2 more segments
                var prefix = parent + "/";
                if (!name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return false;
                }

                var remainder = name[prefix.Length..];
                var slashIdx = remainder.IndexOf('/', StringComparison.Ordinal);
                return slashIdx > 0 && remainder.IndexOf('/', slashIdx + 1) < 0;
            }

            var targetCollection = CollectionPath.Parse($"{parent}/{selector.CollectionId}");
            return targetCollection.IsDirectChildDocument(name);
        }
    }
}
