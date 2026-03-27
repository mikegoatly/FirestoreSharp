using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;

using Value = Google.Cloud.Firestore.V1.Value;

namespace FirestoreSharp.Core.Query;

/// <summary>
/// Applies a <see cref="StructuredAggregationQuery"/> over a set of already-filtered documents.
/// </summary>
internal static class AggregationEngine
{
    /// <summary>
    /// Executes all aggregations in <paramref name="aggregationQuery"/> over <paramref name="documents"/>
    /// and returns an <see cref="AggregationResult"/> whose keys are the (possibly auto-assigned) aliases.
    /// </summary>
    public static AggregationResult Execute(
        StructuredAggregationQuery aggregationQuery,
        IReadOnlyList<Document> documents)
    {
        var result = new AggregationResult();

        // Auto-alias counter is global across the query — only incremented for un-aliased aggregations.
        var autoAliasCounter = 0;

        foreach (var aggregation in aggregationQuery.Aggregations)
        {
            var alias = string.IsNullOrEmpty(aggregation.Alias)
                ? $"field_{autoAliasCounter++}"
                : aggregation.Alias;

            var value = aggregation.OperatorCase switch
            {
                StructuredAggregationQuery.Types.Aggregation.OperatorOneofCase.Count
                    => ComputeCount(documents, aggregation.Count),
                StructuredAggregationQuery.Types.Aggregation.OperatorOneofCase.Sum
                    => ComputeSum(documents, aggregation.Sum.Field.FieldPath),
                StructuredAggregationQuery.Types.Aggregation.OperatorOneofCase.Avg
                    => ComputeAvg(documents, aggregation.Avg.Field.FieldPath),
                _ => throw new InvalidOperationException($"Unknown aggregation operator: {aggregation.OperatorCase}")
            };

            result.AggregateFields[alias] = value;
        }

        return result;
    }

    private static Value ComputeCount(IReadOnlyList<Document> documents, StructuredAggregationQuery.Types.Aggregation.Types.Count count)
    {
        var docs = (IEnumerable<Document>)documents;

        if (count.UpTo is not null)
        {
            docs = docs.Take((int)count.UpTo.Value);
        }

        return new Value { IntegerValue = docs.LongCount() };
    }

    private static Value ComputeSum(IReadOnlyList<Document> documents, string fieldPath)
    {
        var fieldPathParsed = FieldPath.Parse(fieldPath);
        var hasNaN = false;
        var hasDouble = false;
        var intSum = 0L;
        var doubleSum = 0.0;

        foreach (var doc in documents)
        {
            var val = DocumentNavigator.GetValue(doc, fieldPathParsed);
            if (val is null)
            {
                continue;
            }

            switch (val.ValueTypeCase)
            {
                case Value.ValueTypeOneofCase.IntegerValue:
                    // Check for overflow: if adding would overflow, switch to double
                    if (!hasDouble)
                    {
                        try
                        {
                            intSum = checked(intSum + val.IntegerValue);
                        }
                        catch (OverflowException)
                        {
                            hasDouble = true;
                            doubleSum = (double)intSum + val.IntegerValue;
                        }
                    }
                    else
                    {
                        doubleSum += val.IntegerValue;
                    }
                    break;

                case Value.ValueTypeOneofCase.DoubleValue:
                    if (double.IsNaN(val.DoubleValue))
                    {
                        hasNaN = true;
                        hasDouble = true; // ensure we return a double, not an int
                        break;
                    }
                    if (!hasDouble)
                    {
                        hasDouble = true;
                        doubleSum = intSum + val.DoubleValue;
                    }
                    else
                    {
                        doubleSum += val.DoubleValue;
                    }
                    break;

                // All other types (string, null, bool, array, map, etc.) are skipped
            }
        }

        if (hasNaN)
        {
            return new Value { DoubleValue = double.NaN };
        }

        if (hasDouble)
        {
            return new Value { DoubleValue = doubleSum };
        }

        return new Value { IntegerValue = intSum };
    }

    private static Value ComputeAvg(IReadOnlyList<Document> documents, string fieldPath)
    {
        var fieldPathParsed = FieldPath.Parse(fieldPath);
        var hasNaN = false;
        var sum = 0.0;
        var count = 0L;

        foreach (var doc in documents)
        {
            var val = DocumentNavigator.GetValue(doc, fieldPathParsed);
            if (val is null)
            {
                continue;
            }

            switch (val.ValueTypeCase)
            {
                case Value.ValueTypeOneofCase.IntegerValue:
                    sum += val.IntegerValue;
                    count++;
                    break;

                case Value.ValueTypeOneofCase.DoubleValue:
                    if (double.IsNaN(val.DoubleValue))
                    {
                        hasNaN = true;
                    }

                    sum += val.DoubleValue;
                    count++;
                    break;

                // All other types skipped
            }
        }

        // Empty set → NULL
        if (count == 0)
        {
            return new Value { NullValue = NullValue.NullValue };
        }

        if (hasNaN)
        {
            return new Value { DoubleValue = double.NaN };
        }

        return new Value { DoubleValue = sum / count };
    }
}
