using Google.Cloud.Firestore.V1;

using Value = Google.Cloud.Firestore.V1.Value;

namespace FirestoreSharp.Core.Query;

/// <summary>
/// Sorts a sequence of documents according to a <see cref="StructuredQuery"/>'s <c>order_by</c> clause,
/// applying Firestore's implicit ordering rules.
/// </summary>
internal static class QueryOrderer
{
    private const string NameField = "__name__";

    /// <summary>
    /// Sorts <paramref name="documents"/> using the <paramref name="orderBy"/> clause from the query,
    /// applying Firestore's implicit ordering rules:
    /// <list type="bullet">
    /// <item>Any fields referenced by inequality filters that are not already in <c>order_by</c> are prepended.</item>
    /// <item>If <c>__name__</c> is not already the last order clause, it is appended using the same direction as the last explicit clause (or ASCENDING if none).</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<Document> Sort(
        IEnumerable<Document> documents,
        IEnumerable<StructuredQuery.Types.Order> orderBy,
        StructuredQuery.Types.Filter? where)
    {
        var effectiveOrders = BuildEffectiveOrders(orderBy, where);

        if (effectiveOrders.Count == 0)
        {
            // No ordering: stable insertion order (already sorted by ListAsync)
            return documents.ToList();
        }

        IOrderedEnumerable<Document>? ordered = null;

        for (var i = 0; i < effectiveOrders.Count; i++)
        {
            var order = effectiveOrders[i];
            var fieldPath = order.Field.FieldPath;
            var ascending = order.Direction != StructuredQuery.Types.Direction.Descending;

            if (i == 0)
            {
                ordered = ascending
                    ? documents.OrderBy(d => GetSortValue(d, fieldPath), FirestoreValueComparer.Instance)
                    : documents.OrderByDescending(d => GetSortValue(d, fieldPath), FirestoreValueComparer.Instance);
            }
            else
            {
                var captured = order;
                ordered = ascending
                    ? ordered!.ThenBy(d => GetSortValue(d, captured.Field.FieldPath), FirestoreValueComparer.Instance)
                    : ordered!.ThenByDescending(d => GetSortValue(d, captured.Field.FieldPath), FirestoreValueComparer.Instance);
            }
        }

        return ordered!.ToList();
    }

    /// <summary>
    /// Builds the effective order list by applying Firestore's implicit ordering rules.
    /// </summary>
    internal static IReadOnlyList<StructuredQuery.Types.Order> BuildEffectiveOrders(
        IEnumerable<StructuredQuery.Types.Order> orderBy,
        StructuredQuery.Types.Filter? where)
    {
        var orders = orderBy.ToList();

        // Collect field paths already in order_by
        var orderedFields = new HashSet<string>(orders.Select(o => o.Field.FieldPath), StringComparer.Ordinal);

        // Append __name__ if not present (using direction of last order, or ASCENDING if none)
        if (!orderedFields.Contains(NameField))
        {
            var lastDirection = orders.Count > 0
                ? orders[^1].Direction
                : StructuredQuery.Types.Direction.Ascending;

            orders.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = NameField },
                Direction = lastDirection
            });
        }

        return orders;
    }

    private static Value? GetSortValue(Document document, string fieldPath)
    {
        if (fieldPath == NameField)
        {
            return new Value { ReferenceValue = document.Name };
        }

        var path = FieldPath.Parse(fieldPath);
        return DocumentNavigator.GetValue(document, path);
    }
}
