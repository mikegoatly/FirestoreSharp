using FirestoreSharp.Core;
using Google.Cloud.Firestore.V1;
using Xunit;

using Value = Google.Cloud.Firestore.V1.Value;

namespace FirestoreSharp.Tests.Unit;

/// <summary>
/// Deliberately not using the DocumentBuilder here, because that internally uses the DocumentNavigator
/// and we want to be explicit about testing it's behaviour here.
/// </summary>
public sealed class DocumentNavigatorTests
{
    private static Document CreateDocument(Action<Document> configure)
    {
        var doc = new Document { Name = "projects/p/databases/d/documents/col/id" };
        configure(doc);
        return doc;
    }

    [Fact]
    public void GetValue_TopLevel_ReturnsValue()
    {
        var doc = CreateDocument(d => d.Fields["name"] = new Value { StringValue = "Alice" });

        var result = DocumentNavigator.GetValue(doc, FieldPath.Parse("name"));

        Assert.NotNull(result);
        Assert.Equal("Alice", result.StringValue);
    }

    [Fact]
    public void GetValue_Nested_ReturnsValue()
    {
        var doc = CreateDocument(d =>
        {
            d.Fields["address"] = new Value
            {
                MapValue = new MapValue
                {
                    Fields = { ["city"] = new Value { StringValue = "London" } }
                }
            };
        });

        var result = DocumentNavigator.GetValue(doc, FieldPath.Parse("address.city"));

        Assert.NotNull(result);
        Assert.Equal("London", result.StringValue);
    }

    [Fact]
    public void GetValue_DeeplyNested_ReturnsValue()
    {
        var doc = CreateDocument(d =>
        {
            d.Fields["a"] = new Value
            {
                MapValue = new MapValue
                {
                    Fields =
                    {
                        ["b"] = new Value
                        {
                            MapValue = new MapValue
                            {
                                Fields = { ["c"] = new Value { IntegerValue = 42 } }
                            }
                        }
                    }
                }
            };
        });

        var result = DocumentNavigator.GetValue(doc, FieldPath.Parse("a.b.c"));

        Assert.NotNull(result);
        Assert.Equal(42, result.IntegerValue);
    }

    [Fact]
    public void GetValue_MissingTopLevel_ReturnsNull()
    {
        var doc = CreateDocument(_ => { });

        Assert.Null(DocumentNavigator.GetValue(doc, FieldPath.Parse("missing")));
    }

    [Fact]
    public void GetValue_MissingNested_ReturnsNull()
    {
        var doc = CreateDocument(d => d.Fields["name"] = new Value { StringValue = "Alice" });

        Assert.Null(DocumentNavigator.GetValue(doc, FieldPath.Parse("address.city")));
    }

    [Fact]
    public void GetValue_IntermediateNotMap_ReturnsNull()
    {
        var doc = CreateDocument(d => d.Fields["address"] = new Value { StringValue = "not a map" });

        Assert.Null(DocumentNavigator.GetValue(doc, FieldPath.Parse("address.city")));
    }

    [Fact]
    public void SetValue_TopLevel_SetsValue()
    {
        var doc = CreateDocument(_ => { });

        DocumentNavigator.SetValue(doc, FieldPath.Parse("name"), new Value { StringValue = "Bob" });

        Assert.Equal("Bob", doc.Fields["name"].StringValue);
    }

    [Fact]
    public void SetValue_Nested_CreatesIntermediateMaps()
    {
        var doc = CreateDocument(_ => { });

        DocumentNavigator.SetValue(doc, FieldPath.Parse("address.city"), new Value { StringValue = "Paris" });

        Assert.Equal("Paris", doc.Fields["address"].MapValue.Fields["city"].StringValue);
    }

    [Fact]
    public void SetValue_OverwritesExistingValue()
    {
        var doc = CreateDocument(d =>
        {
            d.Fields["address"] = new Value
            {
                MapValue = new MapValue
                {
                    Fields =
                    {
                        ["city"] = new Value { StringValue = "London" },
                        ["zip"] = new Value { StringValue = "SW1" }
                    }
                }
            };
        });

        DocumentNavigator.SetValue(doc, FieldPath.Parse("address.city"), new Value { StringValue = "Paris" });

        Assert.Equal("Paris", doc.Fields["address"].MapValue.Fields["city"].StringValue);
        Assert.Equal("SW1", doc.Fields["address"].MapValue.Fields["zip"].StringValue);
    }

    [Fact]
    public void SetValue_IntermediateNotMap_ReplacesWithMap()
    {
        var doc = CreateDocument(d => d.Fields["address"] = new Value { StringValue = "not a map" });

        DocumentNavigator.SetValue(doc, FieldPath.Parse("address.city"), new Value { StringValue = "Paris" });

        Assert.Equal("Paris", doc.Fields["address"].MapValue.Fields["city"].StringValue);
    }

    [Fact]
    public void RemoveValue_TopLevel_RemovesField()
    {
        var doc = CreateDocument(d =>
        {
            d.Fields["name"] = new Value { StringValue = "Alice" };
            d.Fields["email"] = new Value { StringValue = "a@b.com" };
        });

        var removed = DocumentNavigator.RemoveValue(doc, FieldPath.Parse("email"));

        Assert.True(removed);
        Assert.False(doc.Fields.ContainsKey("email"));
        Assert.True(doc.Fields.ContainsKey("name"));
    }

    [Fact]
    public void RemoveValue_Nested_RemovesField()
    {
        var doc = CreateDocument(d =>
        {
            d.Fields["address"] = new Value
            {
                MapValue = new MapValue
                {
                    Fields =
                    {
                        ["city"] = new Value { StringValue = "London" },
                        ["zip"] = new Value { StringValue = "SW1" }
                    }
                }
            };
        });

        var removed = DocumentNavigator.RemoveValue(doc, FieldPath.Parse("address.city"));

        Assert.True(removed);
        Assert.False(doc.Fields["address"].MapValue.Fields.ContainsKey("city"));
        Assert.True(doc.Fields["address"].MapValue.Fields.ContainsKey("zip"));
    }

    [Fact]
    public void RemoveValue_Nested_CleansUpEmptyIntermediateMaps()
    {
        var doc = CreateDocument(d =>
        {
            d.Fields["address"] = new Value
            {
                MapValue = new MapValue
                {
                    Fields = { ["city"] = new Value { StringValue = "London" } }
                }
            };
        });

        var removed = DocumentNavigator.RemoveValue(doc, FieldPath.Parse("address.city"));

        Assert.True(removed);
        Assert.False(doc.Fields.ContainsKey("address"));
    }

    [Fact]
    public void RemoveValue_Missing_ReturnsFalse()
    {
        var doc = CreateDocument(_ => { });

        Assert.False(DocumentNavigator.RemoveValue(doc, FieldPath.Parse("missing")));
    }

    [Fact]
    public void RemoveValue_MissingNested_ReturnsFalse()
    {
        var doc = CreateDocument(d => d.Fields["name"] = new Value { StringValue = "Alice" });

        Assert.False(DocumentNavigator.RemoveValue(doc, FieldPath.Parse("address.city")));
    }
}
