using Google.Cloud.Firestore.V1;

using Value = Google.Cloud.Firestore.V1.Value;

namespace FirestoreSharp.Core.Query;

/// <summary>
/// Evaluates Firestore cursor positions (<c>start_at</c> / <c>end_at</c>) against documents.
///
/// A cursor carries a list of values that correspond to a prefix of the effective <c>order_by</c> fields.
/// The <c>before</c> flag controls whether the boundary is inclusive or exclusive:
/// <list type="bullet">
/// <item><c>start_at before=true</c>  — keep documents where position &gt;= cursor  (START AT, inclusive)</item>
/// <item><c>start_at before=false</c> — keep documents where position &gt;  cursor  (START AFTER, exclusive)</item>
/// <item><c>end_at   before=true</c>  — keep documents where position &lt;  cursor  (END BEFORE, exclusive)</item>
/// <item><c>end_at   before=false</c> — keep documents where position &lt;= cursor  (END AT, inclusive)</item>
/// </list>
/// </summary>
internal static class QueryCursor
{
    /// <summary>
    /// Returns <c>true</c> if <paramref name="document"/> satisfies the <c>start_at</c> cursor constraint.
    /// </summary>
    public static bool IsAfterStartAt(
        Document document,
        Cursor cursor,
        IReadOnlyList<StructuredQuery.Types.Order> effectiveOrders)
    {
        var cmp = CompareDocumentToCursor(document, cursor, effectiveOrders);
        // before=true  → START AT  → include when doc >= cursor → cmp >= 0
        // before=false → START AFTER → include when doc > cursor → cmp > 0
        return cursor.Before ? cmp >= 0 : cmp > 0;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="document"/> satisfies the <c>end_at</c> cursor constraint.
    /// </summary>
    public static bool IsBeforeEndAt(
        Document document,
        Cursor cursor,
        IReadOnlyList<StructuredQuery.Types.Order> effectiveOrders)
    {
        var cmp = CompareDocumentToCursor(document, cursor, effectiveOrders);
        // before=true  → END BEFORE → include when doc < cursor → cmp < 0
        // before=false → END AT     → include when doc <= cursor → cmp <= 0
        return cursor.Before ? cmp < 0 : cmp <= 0;
    }

    /// <summary>
    /// Compares a document's sort-key tuple against the cursor values using the effective order directions.
    /// Only the first <c>cursor.Values.Count</c> order fields are compared (cursor can be a prefix).
    /// Returns negative if doc &lt; cursor, zero if equal, positive if doc &gt; cursor.
    /// </summary>
    private static int CompareDocumentToCursor(
        Document document,
        Cursor cursor,
        IReadOnlyList<StructuredQuery.Types.Order> effectiveOrders)
    {
        var cursorValues = cursor.Values;
        var fieldCount = Math.Min(cursorValues.Count, effectiveOrders.Count);

        for (var i = 0; i < fieldCount; i++)
        {
            var order = effectiveOrders[i];
            var docValue = GetFieldValue(document, order.Field.FieldPath);
            var cursorValue = cursorValues[i];

            var cmp = FirestoreValueComparer.Instance.Compare(docValue, cursorValue);
            if (cmp == 0)
            {
                continue;
            }

            // Flip sign for descending order
            return order.Direction == StructuredQuery.Types.Direction.Descending ? -cmp : cmp;
        }

        return 0; // equal on all compared fields
    }

    private static Value? GetFieldValue(Document document, string fieldPath)
    {
        if (fieldPath == "__name__")
        {
            return new Value { ReferenceValue = document.Name };
        }

        var path = FieldPath.Parse(fieldPath);
        return DocumentNavigator.GetValue(document, path);
    }
}
