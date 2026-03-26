using Google.Cloud.Firestore.V1;
using Google.Protobuf.Collections;

namespace FirestoreSharp.Core;

/// <summary>
/// Navigates and mutates fields within a <see cref="Document"/> using <see cref="FieldPath"/> instances,
/// supporting nested map traversal.
/// </summary>
internal static class DocumentNavigator
{
    /// <summary>
    /// Gets the value at the given field path, or <c>null</c> if the path does not exist.
    /// </summary>
    public static Value? GetValue(Document document, FieldPath fieldPath)
    {
        var fields = document.Fields;

        for (var i = 0; i < fieldPath.Segments.Count - 1; i++)
        {
            if (!fields.TryGetValue(fieldPath.Segments[i], out var value) || value.ValueTypeCase != Value.ValueTypeOneofCase.MapValue)
            {
                return null;
            }

            fields = value.MapValue.Fields;
        }

        fields.TryGetValue(fieldPath.Segments[^1], out var result);
        return result;
    }

    /// <summary>
    /// Sets the value at the given field path, creating intermediate map values as needed.
    /// </summary>
    public static void SetValue(Document document, FieldPath fieldPath, Value value)
    {
        var fields = document.Fields;

        for (var i = 0; i < fieldPath.Segments.Count - 1; i++)
        {
            var segment = fieldPath.Segments[i];

            if (!fields.TryGetValue(segment, out var existing) || existing.ValueTypeCase != Value.ValueTypeOneofCase.MapValue)
            {
                existing = new Value { MapValue = new MapValue() };
                fields[segment] = existing;
            }

            fields = existing.MapValue.Fields;
        }

        fields[fieldPath.Segments[^1]] = value;
    }

    /// <summary>
    /// Removes the value at the given field path. Returns <c>true</c> if the value existed and was removed.
    /// Cleans up empty intermediate maps.
    /// </summary>
    public static bool RemoveValue(Document document, FieldPath fieldPath)
    {
        return RemoveRecursive(document.Fields, fieldPath, 0);
    }

    private static bool RemoveRecursive(MapField<string, Value> fields, FieldPath fieldPath, int depth)
    {
        var segment = fieldPath.Segments[depth];

        if (depth == fieldPath.Segments.Count - 1)
        {
            return fields.Remove(segment);
        }

        if (!fields.TryGetValue(segment, out var value) || value.ValueTypeCase != Value.ValueTypeOneofCase.MapValue)
        {
            return false;
        }

        var removed = RemoveRecursive(value.MapValue.Fields, fieldPath, depth + 1);

        if (removed && value.MapValue.Fields.Count == 0)
        {
            fields.Remove(segment);
        }

        return removed;
    }
}
