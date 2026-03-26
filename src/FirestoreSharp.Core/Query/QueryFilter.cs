using Google.Cloud.Firestore.V1;

using Value = Google.Cloud.Firestore.V1.Value;

namespace FirestoreSharp.Core.Query;

/// <summary>
/// Evaluates a <see cref="StructuredQuery.Types.Filter"/> against a <see cref="Document"/>.
/// </summary>
internal static class QueryFilter
{
    private const string NameField = "__name__";

    /// <summary>
    /// Returns <c>true</c> if <paramref name="document"/> matches <paramref name="filter"/>.
    /// </summary>
    public static bool Matches(Document document, StructuredQuery.Types.Filter filter)
    {
        return filter.FilterTypeCase switch
        {
            StructuredQuery.Types.Filter.FilterTypeOneofCase.CompositeFilter => MatchesComposite(document, filter.CompositeFilter),
            StructuredQuery.Types.Filter.FilterTypeOneofCase.FieldFilter => MatchesField(document, filter.FieldFilter),
            StructuredQuery.Types.Filter.FilterTypeOneofCase.UnaryFilter => MatchesUnary(document, filter.UnaryFilter),
            _ => true
        };
    }

    private static bool MatchesComposite(Document document, StructuredQuery.Types.CompositeFilter composite)
    {
        return composite.Op switch
        {
            StructuredQuery.Types.CompositeFilter.Types.Operator.And =>
                composite.Filters.All(f => Matches(document, f)),
            StructuredQuery.Types.CompositeFilter.Types.Operator.Or =>
                composite.Filters.Any(f => Matches(document, f)),
            _ => true
        };
    }

    private static bool MatchesField(Document document, StructuredQuery.Types.FieldFilter filter)
    {
        var fieldValue = GetFieldValue(document, filter.Field.FieldPath);

        // Firestore never returns documents with missing fields, regardless of operator
        if (fieldValue is null)
        {
            return false;
        }

        return filter.Op switch
        {
            StructuredQuery.Types.FieldFilter.Types.Operator.Equal =>
                FirestoreValueComparer.Instance.Compare(fieldValue, filter.Value) == 0,

            StructuredQuery.Types.FieldFilter.Types.Operator.NotEqual =>
                !FirestoreValueComparer.IsNaN(fieldValue) &&
                FirestoreValueComparer.Instance.Compare(fieldValue, filter.Value) != 0,

            StructuredQuery.Types.FieldFilter.Types.Operator.LessThan =>
                !FirestoreValueComparer.IsNaN(fieldValue) &&
                FirestoreValueComparer.Instance.Compare(fieldValue, filter.Value) < 0,

            StructuredQuery.Types.FieldFilter.Types.Operator.LessThanOrEqual =>
                !FirestoreValueComparer.IsNaN(fieldValue) &&
                FirestoreValueComparer.Instance.Compare(fieldValue, filter.Value) <= 0,

            StructuredQuery.Types.FieldFilter.Types.Operator.GreaterThan =>
                !FirestoreValueComparer.IsNaN(fieldValue) &&
                FirestoreValueComparer.Instance.Compare(fieldValue, filter.Value) > 0,

            StructuredQuery.Types.FieldFilter.Types.Operator.GreaterThanOrEqual =>
                !FirestoreValueComparer.IsNaN(fieldValue) &&
                FirestoreValueComparer.Instance.Compare(fieldValue, filter.Value) >= 0,

            StructuredQuery.Types.FieldFilter.Types.Operator.ArrayContains =>
                MatchesArrayContains(fieldValue, filter.Value),

            StructuredQuery.Types.FieldFilter.Types.Operator.ArrayContainsAny =>
                MatchesArrayContainsAny(fieldValue, filter.Value),

            StructuredQuery.Types.FieldFilter.Types.Operator.In =>
                MatchesIn(fieldValue, filter.Value),

            StructuredQuery.Types.FieldFilter.Types.Operator.NotIn =>
                MatchesNotIn(fieldValue, filter.Value),

            _ => false
        };
    }

    private static bool MatchesUnary(Document document, StructuredQuery.Types.UnaryFilter filter)
    {
        var fieldValue = GetFieldValue(document, filter.Field.FieldPath);

        return filter.Op switch
        {
            StructuredQuery.Types.UnaryFilter.Types.Operator.IsNull =>
                fieldValue is not null && FirestoreValueComparer.IsNull(fieldValue),

            StructuredQuery.Types.UnaryFilter.Types.Operator.IsNotNull =>
                fieldValue is not null && !FirestoreValueComparer.IsNull(fieldValue),

            StructuredQuery.Types.UnaryFilter.Types.Operator.IsNan =>
                fieldValue is not null && FirestoreValueComparer.IsNaN(fieldValue),

            StructuredQuery.Types.UnaryFilter.Types.Operator.IsNotNan =>
                fieldValue is not null && !FirestoreValueComparer.IsNaN(fieldValue),

            _ => false
        };
    }

    private static bool MatchesArrayContains(Value fieldValue, Value searchValue)
    {
        if (fieldValue.ValueTypeCase != Value.ValueTypeOneofCase.ArrayValue)
        {
            return false;
        }

        return fieldValue.ArrayValue.Values.Any(v =>
            FirestoreValueComparer.Instance.Compare(v, searchValue) == 0);
    }

    private static bool MatchesArrayContainsAny(Value fieldValue, Value searchValues)
    {
        if (fieldValue.ValueTypeCase != Value.ValueTypeOneofCase.ArrayValue ||
            searchValues.ValueTypeCase != Value.ValueTypeOneofCase.ArrayValue)
        {
            return false;
        }

        return fieldValue.ArrayValue.Values.Any(item =>
            searchValues.ArrayValue.Values.Any(target =>
                FirestoreValueComparer.Instance.Compare(item, target) == 0));
    }

    private static bool MatchesIn(Value fieldValue, Value allowedValues)
    {
        if (allowedValues.ValueTypeCase != Value.ValueTypeOneofCase.ArrayValue)
        {
            return false;
        }

        return allowedValues.ArrayValue.Values.Any(v =>
            FirestoreValueComparer.Instance.Compare(fieldValue, v) == 0);
    }

    private static bool MatchesNotIn(Value fieldValue, Value disallowedValues)
    {
        if (disallowedValues.ValueTypeCase != Value.ValueTypeOneofCase.ArrayValue)
        {
            return true;
        }

        return !disallowedValues.ArrayValue.Values.Any(v =>
            FirestoreValueComparer.Instance.Compare(fieldValue, v) == 0);
    }

    /// <summary>
    /// Returns the value of the field identified by <paramref name="fieldPath"/> in the document.
    /// The special path <c>__name__</c> returns a reference value using the document's name.
    /// Returns <c>null</c> if the field does not exist.
    /// </summary>
    internal static Value? GetFieldValue(Document document, string fieldPath)
    {
        if (fieldPath == NameField)
        {
            return new Value { ReferenceValue = document.Name };
        }

        var path = FieldPath.Parse(fieldPath);
        return DocumentNavigator.GetValue(document, path);
    }
}
