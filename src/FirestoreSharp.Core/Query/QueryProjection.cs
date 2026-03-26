using Google.Cloud.Firestore.V1;

namespace FirestoreSharp.Core.Query;

/// <summary>
/// Applies a <see cref="StructuredQuery.Types.Projection"/> to documents,
/// returning new documents containing only the requested fields.
/// An empty projection returns documents unchanged.
/// </summary>
internal static class QueryProjection
{
    private const string NameField = "__name__";

    /// <summary>
    /// Applies <paramref name="projection"/> to <paramref name="document"/>.
    /// Returns the original document if the projection is null or has no fields.
    /// </summary>
    public static Document Apply(Document document, StructuredQuery.Types.Projection? projection)
    {
        if (projection is null || projection.Fields.Count == 0)
        {
            return document;
        }

        var result = new Document
        {
            Name = document.Name,
            CreateTime = document.CreateTime,
            UpdateTime = document.UpdateTime
        };

        foreach (var fieldRef in projection.Fields)
        {
            if (fieldRef.FieldPath == NameField)
            {
                // __name__ is represented by Name on the document — nothing to copy to fields
                continue;
            }

            var path = FieldPath.Parse(fieldRef.FieldPath);
            var value = DocumentNavigator.GetValue(document, path);

            if (value is not null)
            {
                DocumentNavigator.SetValue(result, path, value);
            }
        }

        return result;
    }
}
