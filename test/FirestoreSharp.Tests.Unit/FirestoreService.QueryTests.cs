using FirestoreSharp.Tests.Unit.Builders;
using Google.Cloud.Firestore.V1;
using Grpc.Core;
using Xunit;

using Value = Google.Cloud.Firestore.V1.Value;

using Microsoft.AspNetCore.Mvc.Testing;

namespace FirestoreSharp.Tests.Unit;

public sealed class FirestoreServiceQueryTests(WebApplicationFactory<Program> factory) : FirestoreServiceTestBase(factory)
{

    private static async Task<List<RunQueryResponse>> RunQueryAsync(
        Firestore.FirestoreClient client,
        RunQueryRequest request,
        CancellationToken cancellationToken)
    {
        var responses = new List<RunQueryResponse>();
        using var call = client.RunQuery(request, cancellationToken: cancellationToken);
        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            responses.Add(response);
        }
        return responses;
    }

    [Fact]
    public async Task RunQuery_NoFilter_ReturnsAllDocumentsInCollection()
    {
        var alice = new DocumentBuilder().WithCollection("rq-nofilter").WithId("alice").WithField("name", "Alice");
        var bob = new DocumentBuilder().WithCollection("rq-nofilter").WithId("bob").WithField("name", "Bob");

        await Client.CreateDocumentAsync(alice.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await Client.CreateDocumentAsync(bob.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new DocumentBuilder().WithCollection("rq-nofilter").BuildRunQueryRequest();
        var responses = await RunQueryAsync(Client, request, TestContext.Current.CancellationToken);

        // Final done=true response is included; filter it out to count real documents
        var docResponses = responses.Where(r => r.Document is not null).ToList();
        Assert.Equal(2, docResponses.Count);
        Assert.Contains(docResponses, r => r.Document.Name == alice.ExpectedName);
        Assert.Contains(docResponses, r => r.Document.Name == bob.ExpectedName);
    }

    [Fact]
    public async Task RunQuery_EqualityFilter_ReturnsMatchingDocuments()
    {
        var active1 = new DocumentBuilder().WithCollection("rq-status").WithId("active-1").WithField("status", "active");
        var active2 = new DocumentBuilder().WithCollection("rq-status").WithId("active-2").WithField("status", "active");
        var inactive = new DocumentBuilder().WithCollection("rq-status").WithId("inactive-1").WithField("status", "inactive");

        await Client.CreateDocumentAsync(active1.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await Client.CreateDocumentAsync(active2.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await Client.CreateDocumentAsync(inactive.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new DocumentBuilder().WithCollection("rq-status").BuildRunQueryRequest(query =>
        {
            query.Where = new StructuredQuery.Types.Filter
            {
                FieldFilter = new StructuredQuery.Types.FieldFilter
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "status" },
                    Op = StructuredQuery.Types.FieldFilter.Types.Operator.Equal,
                    Value = new Value { StringValue = "active" }
                }
            };
        });

        var responses = await RunQueryAsync(Client, request, TestContext.Current.CancellationToken);
        var docs = responses.Where(r => r.Document is not null).ToList();

        Assert.Equal(2, docs.Count);
        Assert.All(docs, r => Assert.Equal("active", r.Document.Fields["status"].StringValue));
    }

    [Fact]
    public async Task RunQuery_InequalityFilter_ReturnsMatchingDocuments()
    {
        var u1 = new DocumentBuilder().WithCollection("rq-ineq").WithId("u1").WithField("score", 10L);
        var u2 = new DocumentBuilder().WithCollection("rq-ineq").WithId("u2").WithField("score", 20L);
        var u3 = new DocumentBuilder().WithCollection("rq-ineq").WithId("u3").WithField("score", 30L);

        await Client.CreateDocumentAsync(u1.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await Client.CreateDocumentAsync(u2.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await Client.CreateDocumentAsync(u3.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new DocumentBuilder().WithCollection("rq-ineq").BuildRunQueryRequest(query =>
        {
            query.Where = new StructuredQuery.Types.Filter
            {
                FieldFilter = new StructuredQuery.Types.FieldFilter
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" },
                    Op = StructuredQuery.Types.FieldFilter.Types.Operator.GreaterThan,
                    Value = new Value { IntegerValue = 15 }
                }
            };
        });

        var responses = await RunQueryAsync(Client, request, TestContext.Current.CancellationToken);
        var docs = responses.Where(r => r.Document is not null).ToList();

        Assert.Equal(2, docs.Count);
        Assert.All(docs, r => Assert.True(r.Document.Fields["score"].IntegerValue > 15));
    }

    [Fact]
    public async Task RunQuery_OrderBy_ReturnsSortedResults()
    {
        var u3 = new DocumentBuilder().WithCollection("rq-order").WithId("u3").WithField("rank", 3L);
        var u1 = new DocumentBuilder().WithCollection("rq-order").WithId("u1").WithField("rank", 1L);
        var u2 = new DocumentBuilder().WithCollection("rq-order").WithId("u2").WithField("rank", 2L);

        await Client.CreateDocumentAsync(u3.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await Client.CreateDocumentAsync(u1.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await Client.CreateDocumentAsync(u2.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new DocumentBuilder().WithCollection("rq-order").BuildRunQueryRequest(query =>
        {
            query.OrderBy.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = "rank" },
                Direction = StructuredQuery.Types.Direction.Ascending
            });
        });

        var responses = await RunQueryAsync(Client, request, TestContext.Current.CancellationToken);
        var docs = responses.Where(r => r.Document is not null).ToList();

        Assert.Equal(3, docs.Count);
        Assert.Equal(1L, docs[0].Document.Fields["rank"].IntegerValue);
        Assert.Equal(2L, docs[1].Document.Fields["rank"].IntegerValue);
        Assert.Equal(3L, docs[2].Document.Fields["rank"].IntegerValue);
    }

    [Fact]
    public async Task RunQuery_Limit_CapsResults()
    {
        for (var i = 1; i <= 5; i++)
        {
            var doc = new DocumentBuilder().WithCollection("rq-limit").WithId($"d{i:D2}").WithField("n", (long)i);
            await Client.CreateDocumentAsync(doc.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        }

        var request = new DocumentBuilder().WithCollection("rq-limit").BuildRunQueryRequest(query =>
        {
            query.OrderBy.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = "n" },
                Direction = StructuredQuery.Types.Direction.Ascending
            });
            query.Limit = 3;
        });

        var docs = (await RunQueryAsync(Client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).ToList();

        Assert.Equal(3, docs.Count);
        Assert.Equal(1L, docs[0].Document.Fields["n"].IntegerValue);
    }

    [Fact]
    public async Task RunQuery_OffsetAndLimit_PagesResults()
    {
        for (var i = 1; i <= 5; i++)
        {
            var doc = new DocumentBuilder().WithCollection("rq-page").WithId($"p{i:D2}").WithField("n", (long)i);
            await Client.CreateDocumentAsync(doc.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        }

        var request = new DocumentBuilder().WithCollection("rq-page").BuildRunQueryRequest(query =>
        {
            query.OrderBy.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = "n" },
                Direction = StructuredQuery.Types.Direction.Ascending
            });
            query.Offset = 2;
            query.Limit = 2;
        });

        var docs = (await RunQueryAsync(Client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).ToList();

        Assert.Equal(2, docs.Count);
        Assert.Equal(3L, docs[0].Document.Fields["n"].IntegerValue);
        Assert.Equal(4L, docs[1].Document.Fields["n"].IntegerValue);
    }

    [Fact]
    public async Task RunQuery_Select_ReturnsOnlySpecifiedFields()
    {
        var doc = new DocumentBuilder()
            .WithCollection("rq-select").WithId("s1")
            .WithField("name", "Alice")
            .WithField("email", "alice@example.com")
            .WithField("age", 30L);

        await Client.CreateDocumentAsync(doc.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new DocumentBuilder().WithCollection("rq-select").BuildRunQueryRequest(query =>
        {
            query.Select = new StructuredQuery.Types.Projection();
            query.Select.Fields.Add(new StructuredQuery.Types.FieldReference { FieldPath = "name" });
            query.Select.Fields.Add(new StructuredQuery.Types.FieldReference { FieldPath = "email" });
        });

        var docs = (await RunQueryAsync(Client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).ToList();

        var result = Assert.Single(docs).Document;
        Assert.True(result.Fields.ContainsKey("name"));
        Assert.True(result.Fields.ContainsKey("email"));
        Assert.False(result.Fields.ContainsKey("age"));
    }

    [Fact]
    public async Task RunQuery_NoMatchingDocuments_SendsDoneResponse()
    {
        var request = new DocumentBuilder().WithCollection("rq-empty").BuildRunQueryRequest(query =>
        {
            query.Where = new StructuredQuery.Types.Filter
            {
                FieldFilter = new StructuredQuery.Types.FieldFilter
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "status" },
                    Op = StructuredQuery.Types.FieldFilter.Types.Operator.Equal,
                    Value = new Value { StringValue = "active" }
                }
            };
        });

        var responses = await RunQueryAsync(Client, request, TestContext.Current.CancellationToken);

        Assert.Single(responses); // Only the done=true message
        Assert.True(responses[0].Done);
        Assert.Null(responses[0].Document);
    }

    [Fact]
    public async Task RunQuery_CompositeAndFilter_ReturnsMatchingDocuments()
    {
        var match = new DocumentBuilder().WithCollection("rq-and").WithId("match").WithField("active", true).WithField("score", 50L);
        var noMatch1 = new DocumentBuilder().WithCollection("rq-and").WithId("no-active").WithField("active", false).WithField("score", 50L);
        var noMatch2 = new DocumentBuilder().WithCollection("rq-and").WithId("no-score").WithField("active", true).WithField("score", 5L);

        await Client.CreateDocumentAsync(match.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await Client.CreateDocumentAsync(noMatch1.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await Client.CreateDocumentAsync(noMatch2.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new DocumentBuilder().WithCollection("rq-and").BuildRunQueryRequest(query =>
        {
            var composite = new StructuredQuery.Types.CompositeFilter
            {
                Op = StructuredQuery.Types.CompositeFilter.Types.Operator.And
            };
            composite.Filters.Add(new StructuredQuery.Types.Filter
            {
                FieldFilter = new StructuredQuery.Types.FieldFilter
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "active" },
                    Op = StructuredQuery.Types.FieldFilter.Types.Operator.Equal,
                    Value = new Value { BooleanValue = true }
                }
            });
            composite.Filters.Add(new StructuredQuery.Types.Filter
            {
                FieldFilter = new StructuredQuery.Types.FieldFilter
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" },
                    Op = StructuredQuery.Types.FieldFilter.Types.Operator.GreaterThanOrEqual,
                    Value = new Value { IntegerValue = 20 }
                }
            });
            query.Where = new StructuredQuery.Types.Filter { CompositeFilter = composite };
        });

        var docs = (await RunQueryAsync(Client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).ToList();

        Assert.Single(docs);
        Assert.Equal(match.ExpectedName, docs[0].Document.Name);
    }

    [Fact]
    public async Task RunQuery_AllDescendants_ReturnsDocumentsInSubcollections()
    {
        var parent = "projects/test-project/databases/(default)/documents";

        // Create a document in a subcollection: rq-groups/g1/members/m1
        var member = new DocumentBuilder()
            .WithParent($"{parent}/rq-groups/g1")
            .WithCollection("members")
            .WithId("m1")
            .WithField("role", "admin");

        await Client.CreateDocumentAsync(member.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new RunQueryRequest
        {
            Parent = parent,
            StructuredQuery = new StructuredQuery()
        };
        request.StructuredQuery.From.Add(new StructuredQuery.Types.CollectionSelector
        {
            CollectionId = "members",
            AllDescendants = true
        });

        var docs = (await RunQueryAsync(Client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).ToList();

        Assert.Single(docs);
        Assert.Equal(member.ExpectedName, docs[0].Document.Name);
    }

    [Fact]
    public async Task RunQuery_AllDocuments_SendsReadTimeOnEachResult()
    {
        var doc = new DocumentBuilder().WithCollection("rq-readtime").WithId("rt1").WithField("x", "y");
        await Client.CreateDocumentAsync(doc.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = new DocumentBuilder().WithCollection("rq-readtime").BuildRunQueryRequest();
        var responses = await RunQueryAsync(Client, request, TestContext.Current.CancellationToken);

        Assert.All(responses, r => Assert.NotNull(r.ReadTime));
    }

    // ── Cursors ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunQuery_StartAt_Inclusive_ReturnsDocumentsFromPosition()
    {
        var col = "rq-cursor-start-at";
        for (var i = 1; i <= 5; i++)
        {
            await Client.CreateDocumentAsync(
                new DocumentBuilder().WithCollection(col).WithId($"d{i:D2}").WithField("n", (long)i).BuildCreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // START AT n=3 (inclusive) → expect docs with n = 3, 4, 5
        var request = new DocumentBuilder().WithCollection(col).BuildRunQueryRequest(query =>
        {
            query.OrderBy.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = "n" },
                Direction = StructuredQuery.Types.Direction.Ascending
            });
            query.StartAt = new Cursor
            {
                Before = true, // START AT (inclusive)
                Values = { new Value { IntegerValue = 3 } }
            };
        });

        var docs = (await RunQueryAsync(Client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).Select(r => r.Document).ToList();

        Assert.Equal(3, docs.Count);
        Assert.Equal(3L, docs[0].Fields["n"].IntegerValue);
        Assert.Equal(4L, docs[1].Fields["n"].IntegerValue);
        Assert.Equal(5L, docs[2].Fields["n"].IntegerValue);
    }

    [Fact]
    public async Task RunQuery_StartAfter_Exclusive_ExcludesStartDocument()
    {
        var col = "rq-cursor-start-after";
        for (var i = 1; i <= 5; i++)
        {
            await Client.CreateDocumentAsync(
                new DocumentBuilder().WithCollection(col).WithId($"d{i:D2}").WithField("n", (long)i).BuildCreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // START AFTER n=3 (exclusive) → expect docs with n = 4, 5
        var request = new DocumentBuilder().WithCollection(col).BuildRunQueryRequest(query =>
        {
            query.OrderBy.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = "n" },
                Direction = StructuredQuery.Types.Direction.Ascending
            });
            query.StartAt = new Cursor
            {
                Before = false, // START AFTER (exclusive)
                Values = { new Value { IntegerValue = 3 } }
            };
        });

        var docs = (await RunQueryAsync(Client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).Select(r => r.Document).ToList();

        Assert.Equal(2, docs.Count);
        Assert.Equal(4L, docs[0].Fields["n"].IntegerValue);
        Assert.Equal(5L, docs[1].Fields["n"].IntegerValue);
    }

    [Fact]
    public async Task RunQuery_EndAt_Inclusive_ReturnsDocumentsUpToPosition()
    {
        var col = "rq-cursor-end-at";
        for (var i = 1; i <= 5; i++)
        {
            await Client.CreateDocumentAsync(
                new DocumentBuilder().WithCollection(col).WithId($"d{i:D2}").WithField("n", (long)i).BuildCreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // END AT n=3 (inclusive) → expect docs with n = 1, 2, 3
        var request = new DocumentBuilder().WithCollection(col).BuildRunQueryRequest(query =>
        {
            query.OrderBy.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = "n" },
                Direction = StructuredQuery.Types.Direction.Ascending
            });
            query.EndAt = new Cursor
            {
                Before = false, // END AT (inclusive)
                Values = { new Value { IntegerValue = 3 } }
            };
        });

        var docs = (await RunQueryAsync(Client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).Select(r => r.Document).ToList();

        Assert.Equal(3, docs.Count);
        Assert.Equal(1L, docs[0].Fields["n"].IntegerValue);
        Assert.Equal(2L, docs[1].Fields["n"].IntegerValue);
        Assert.Equal(3L, docs[2].Fields["n"].IntegerValue);
    }

    [Fact]
    public async Task RunQuery_EndBefore_Exclusive_ExcludesEndDocument()
    {
        var col = "rq-cursor-end-before";
        for (var i = 1; i <= 5; i++)
        {
            await Client.CreateDocumentAsync(
                new DocumentBuilder().WithCollection(col).WithId($"d{i:D2}").WithField("n", (long)i).BuildCreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // END BEFORE n=3 (exclusive) → expect docs with n = 1, 2
        var request = new DocumentBuilder().WithCollection(col).BuildRunQueryRequest(query =>
        {
            query.OrderBy.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = "n" },
                Direction = StructuredQuery.Types.Direction.Ascending
            });
            query.EndAt = new Cursor
            {
                Before = true, // END BEFORE (exclusive)
                Values = { new Value { IntegerValue = 3 } }
            };
        });

        var docs = (await RunQueryAsync(Client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).Select(r => r.Document).ToList();

        Assert.Equal(2, docs.Count);
        Assert.Equal(1L, docs[0].Fields["n"].IntegerValue);
        Assert.Equal(2L, docs[1].Fields["n"].IntegerValue);
    }

    [Fact]
    public async Task RunQuery_StartAtEndAt_DefinesWindow()
    {
        var col = "rq-cursor-window";
        for (var i = 1; i <= 7; i++)
        {
            await Client.CreateDocumentAsync(
                new DocumentBuilder().WithCollection(col).WithId($"d{i:D2}").WithField("n", (long)i).BuildCreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // START AT n=2 (inclusive), END AT n=5 (inclusive) → expect n = 2, 3, 4, 5
        var request = new DocumentBuilder().WithCollection(col).BuildRunQueryRequest(query =>
        {
            query.OrderBy.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = "n" },
                Direction = StructuredQuery.Types.Direction.Ascending
            });
            query.StartAt = new Cursor { Before = true, Values = { new Value { IntegerValue = 2 } } };
            query.EndAt = new Cursor { Before = false, Values = { new Value { IntegerValue = 5 } } };
        });

        var docs = (await RunQueryAsync(Client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).Select(r => r.Document).ToList();

        Assert.Equal(4, docs.Count);
        Assert.Equal(2L, docs[0].Fields["n"].IntegerValue);
        Assert.Equal(5L, docs[3].Fields["n"].IntegerValue);
    }

    [Fact]
    public async Task RunQuery_CursorWithDescendingOrder_Works()
    {
        var col = "rq-cursor-desc";
        for (var i = 1; i <= 5; i++)
        {
            await Client.CreateDocumentAsync(
                new DocumentBuilder().WithCollection(col).WithId($"d{i:D2}").WithField("n", (long)i).BuildCreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // DESC order: 5, 4, 3, 2, 1. START AT n=4 (inclusive) → 4, 3, 2, 1
        var request = new DocumentBuilder().WithCollection(col).BuildRunQueryRequest(query =>
        {
            query.OrderBy.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = "n" },
                Direction = StructuredQuery.Types.Direction.Descending
            });
            query.StartAt = new Cursor { Before = true, Values = { new Value { IntegerValue = 4 } } };
        });

        var docs = (await RunQueryAsync(Client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).Select(r => r.Document).ToList();

        Assert.Equal(4, docs.Count);
        Assert.Equal(4L, docs[0].Fields["n"].IntegerValue);
        Assert.Equal(1L, docs[3].Fields["n"].IntegerValue);
    }

    [Fact]
    public async Task RunQuery_CursorPrefixValues_MatchesOnPartialKey()
    {
        var col = "rq-cursor-prefix";
        await Client.CreateDocumentAsync(new DocumentBuilder().WithCollection(col).WithId("a").WithField("score", 10L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await Client.CreateDocumentAsync(new DocumentBuilder().WithCollection(col).WithId("b").WithField("score", 10L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await Client.CreateDocumentAsync(new DocumentBuilder().WithCollection(col).WithId("c").WithField("score", 10L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await Client.CreateDocumentAsync(new DocumentBuilder().WithCollection(col).WithId("d").WithField("score", 20L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        // ORDER BY score ASC, __name__ ASC. Cursor prefix: score=10 (before=false, START AFTER).
        // With only score=10 in cursor and before=false, this starts after ALL docs with score=10.
        var request = new DocumentBuilder().WithCollection(col).BuildRunQueryRequest(query =>
        {
            query.OrderBy.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" },
                Direction = StructuredQuery.Types.Direction.Ascending
            });
            // Cursor has only score value (prefix of the full [score, __name__] order)
            query.StartAt = new Cursor { Before = false, Values = { new Value { IntegerValue = 10 } } };
        });

        var docs = (await RunQueryAsync(Client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).Select(r => r.Document).ToList();

        // score=10 docs are all equal to cursor on the compared prefix field, cmp=0 → excluded (before=false)
        // Only score=20 doc remains
        Assert.Single(docs);
        Assert.Equal(20L, docs[0].Fields["score"].IntegerValue);
    }

    [Fact]
    public async Task RunQuery_CursorOn__name__Field_FiltersCorrectly()
    {
        var col = "rq-cursor-name";
        // doc IDs are alphabetical: a, b, c, d, e
        foreach (var id in new[] { "a", "b", "c", "d", "e" })
        {
            await Client.CreateDocumentAsync(
                new DocumentBuilder().WithCollection(col).WithId(id).WithField("v", id).BuildCreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var cursorDocName = new DocumentBuilder().WithCollection(col).WithId("c").ExpectedName;

        // ORDER BY __name__ ASC, START AFTER __name__="c" → expect d, e
        var request = new DocumentBuilder().WithCollection(col).BuildRunQueryRequest(query =>
        {
            query.OrderBy.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = "__name__" },
                Direction = StructuredQuery.Types.Direction.Ascending
            });
            query.StartAt = new Cursor
            {
                Before = false, // START AFTER (exclusive)
                Values = { new Value { ReferenceValue = cursorDocName } }
            };
        });

        var docs = (await RunQueryAsync(Client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).Select(r => r.Document).ToList();

        Assert.Equal(2, docs.Count);
        Assert.Equal("d", docs[0].Fields["v"].StringValue);
        Assert.Equal("e", docs[1].Fields["v"].StringValue);
    }

    [Fact]
    public async Task RunQuery_CursorWithMissingField_TreatsAsNull()
    {
        var col = "rq-cursor-missing-field";
        // doc1 has no "score" field, doc2 has score=10, doc3 has score=20
        await Client.CreateDocumentAsync(
            new DocumentBuilder().WithCollection(col).WithId("doc1").WithField("v", "no-score").BuildCreateRequest(),
            cancellationToken: TestContext.Current.CancellationToken);
        await Client.CreateDocumentAsync(
            new DocumentBuilder().WithCollection(col).WithId("doc2").WithField("score", 10L).BuildCreateRequest(),
            cancellationToken: TestContext.Current.CancellationToken);
        await Client.CreateDocumentAsync(
            new DocumentBuilder().WithCollection(col).WithId("doc3").WithField("score", 20L).BuildCreateRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Firestore value ordering: null < numbers. START AFTER null (before=false) → excludes doc1,
        // includes doc2 and doc3 (which have actual score values > null).
        var request = new DocumentBuilder().WithCollection(col).BuildRunQueryRequest(query =>
        {
            query.OrderBy.Add(new StructuredQuery.Types.Order
            {
                Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" },
                Direction = StructuredQuery.Types.Direction.Ascending
            });
            query.StartAt = new Cursor
            {
                Before = false, // START AFTER null
                Values = { new Value { NullValue = Google.Protobuf.WellKnownTypes.NullValue.NullValue } }
            };
        });

        var docs = (await RunQueryAsync(Client, request, TestContext.Current.CancellationToken))
            .Where(r => r.Document is not null).Select(r => r.Document).ToList();

        // doc1 (missing field = null) is excluded; doc2 and doc3 remain
        Assert.Equal(2, docs.Count);
        Assert.All(docs, d => Assert.True(d.Fields.ContainsKey("score")));
    }
}
