using FirestoreSharp.Tests.Unit.Builders;
using Google.Cloud.Firestore.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

using Value = Google.Cloud.Firestore.V1.Value;

namespace FirestoreSharp.Tests.Unit;

public sealed class FirestoreServiceAggregationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly Firestore.FirestoreClient _client;

    public FirestoreServiceAggregationTests(WebApplicationFactory<Program> factory)
    {
        var httpClient = factory.CreateDefaultClient();
        _channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });
        _client = new Firestore.FirestoreClient(_channel);
    }

    public void Dispose() => _channel.Dispose();

    private static async Task<List<RunAggregationQueryResponse>> RunAggregationAsync(
        Firestore.FirestoreClient client,
        RunAggregationQueryRequest request,
        CancellationToken cancellationToken)
    {
        var responses = new List<RunAggregationQueryResponse>();
        using var call = client.RunAggregationQuery(request, cancellationToken: cancellationToken);
        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            responses.Add(response);
        }
        return responses;
    }

    // ── COUNT ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Count_ReturnsDocumentCount()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-count-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        for (var i = 1; i <= 3; i++)
        {
            await _client.CreateDocumentAsync(
                builder.WithId($"doc{i}").BuildCreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var request = builder.BuildAggregationQueryRequest(q =>
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Count = new StructuredAggregationQuery.Types.Aggregation.Types.Count(),
                Alias = "total"
            }));

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var result = Assert.Single(responses).Result;
        Assert.Equal(3L, result.AggregateFields["total"].IntegerValue);
    }

    [Fact]
    public async Task Count_EmptyCollection_ReturnsZero()
    {
        var col = $"agg-count-empty-{Guid.NewGuid():N}";
        var builder = new DocumentBuilder().WithCollection(col);

        var request = builder.BuildAggregationQueryRequest(q =>
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Count = new StructuredAggregationQuery.Types.Aggregation.Types.Count(),
                Alias = "total"
            }));

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var result = Assert.Single(responses).Result;
        Assert.Equal(0L, result.AggregateFields["total"].IntegerValue);
    }

    [Fact]
    public async Task Count_UpTo_CapsResult()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-count-upto-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        for (var i = 1; i <= 5; i++)
        {
            await _client.CreateDocumentAsync(
                builder.WithId($"doc{i}").BuildCreateRequest(),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var request = builder.BuildAggregationQueryRequest(q =>
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Count = new StructuredAggregationQuery.Types.Aggregation.Types.Count
                {
                    UpTo = 3L
                },
                Alias = "capped"
            }));

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var result = Assert.Single(responses).Result;
        Assert.Equal(3L, result.AggregateFields["capped"].IntegerValue);
    }

    [Fact]
    public async Task Count_WithInnerQueryFilter_CountsMatchingDocuments()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-count-filter-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        await _client.CreateDocumentAsync(builder.WithId("a").WithField("active", true).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("b").WithField("active", true).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("c").WithField("active", false).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = builder.BuildAggregationQueryRequest(q =>
        {
            q.StructuredQuery.Where = new StructuredQuery.Types.Filter
            {
                FieldFilter = new StructuredQuery.Types.FieldFilter
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "active" },
                    Op = StructuredQuery.Types.FieldFilter.Types.Operator.Equal,
                    Value = new Value { BooleanValue = true }
                }
            };
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Count = new StructuredAggregationQuery.Types.Aggregation.Types.Count(),
                Alias = "active_count"
            });
        });

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var result = Assert.Single(responses).Result;
        Assert.Equal(2L, result.AggregateFields["active_count"].IntegerValue);
    }

    [Fact]
    public async Task Count_WithInnerQueryLimit_CountsLimitedDocuments()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-count-limit-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        for (var i = 1; i <= 4; i++)
        {
            await _client.CreateDocumentAsync(builder.WithId($"doc{i}").BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        }

        var request = builder.BuildAggregationQueryRequest(q =>
        {
            q.StructuredQuery.Limit = 2;
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Count = new StructuredAggregationQuery.Types.Aggregation.Types.Count(),
                Alias = "limited"
            });
        });

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var result = Assert.Single(responses).Result;
        Assert.Equal(2L, result.AggregateFields["limited"].IntegerValue);
    }

    // ── SUM ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sum_IntegerValues_ReturnsInteger()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-sum-int-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        await _client.CreateDocumentAsync(builder.WithId("a").WithField("score", 10L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("b").WithField("score", 20L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("c").WithField("score", 30L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = builder.BuildAggregationQueryRequest(q =>
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Sum = new StructuredAggregationQuery.Types.Aggregation.Types.Sum
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" }
                },
                Alias = "total_score"
            }));

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var result = Assert.Single(responses).Result;
        var value = result.AggregateFields["total_score"];
        Assert.Equal(Value.ValueTypeOneofCase.IntegerValue, value.ValueTypeCase);
        Assert.Equal(60L, value.IntegerValue);
    }

    [Fact]
    public async Task Sum_MixedIntAndDouble_ReturnsDouble()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-sum-mixed-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        await _client.CreateDocumentAsync(builder.WithId("a").WithField("score", 10L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("b").WithField("score", 2.5).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = builder.BuildAggregationQueryRequest(q =>
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Sum = new StructuredAggregationQuery.Types.Aggregation.Types.Sum
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" }
                },
                Alias = "total"
            }));

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var value = Assert.Single(responses).Result.AggregateFields["total"];
        Assert.Equal(Value.ValueTypeOneofCase.DoubleValue, value.ValueTypeCase);
        Assert.Equal(12.5, value.DoubleValue);
    }

    [Fact]
    public async Task Sum_EmptySet_ReturnsZero()
    {
        var col = $"agg-sum-empty-{Guid.NewGuid():N}";
        var builder = new DocumentBuilder().WithCollection(col);

        var request = builder.BuildAggregationQueryRequest(q =>
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Sum = new StructuredAggregationQuery.Types.Aggregation.Types.Sum
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" }
                },
                Alias = "total"
            }));

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var value = Assert.Single(responses).Result.AggregateFields["total"];
        Assert.Equal(Value.ValueTypeOneofCase.IntegerValue, value.ValueTypeCase);
        Assert.Equal(0L, value.IntegerValue);
    }

    [Fact]
    public async Task Sum_NonNumericValuesSkipped()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-sum-skip-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        await _client.CreateDocumentAsync(builder.WithId("a").WithField("score", 10L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("b").WithField("score", "not-a-number").BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("c").WithNullField("score").BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("d").WithField("other", 99L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = builder.BuildAggregationQueryRequest(q =>
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Sum = new StructuredAggregationQuery.Types.Aggregation.Types.Sum
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" }
                },
                Alias = "total"
            }));

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var value = Assert.Single(responses).Result.AggregateFields["total"];
        Assert.Equal(Value.ValueTypeOneofCase.IntegerValue, value.ValueTypeCase);
        Assert.Equal(10L, value.IntegerValue);
    }

    [Fact]
    public async Task Sum_NaN_ReturnsNaN()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-sum-nan-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        await _client.CreateDocumentAsync(builder.WithId("a").WithField("score", 10L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("b").WithField("score", double.NaN).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = builder.BuildAggregationQueryRequest(q =>
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Sum = new StructuredAggregationQuery.Types.Aggregation.Types.Sum
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" }
                },
                Alias = "total"
            }));

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var value = Assert.Single(responses).Result.AggregateFields["total"];
        Assert.Equal(Value.ValueTypeOneofCase.DoubleValue, value.ValueTypeCase);
        Assert.True(double.IsNaN(value.DoubleValue));
    }

    // ── AVG ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Avg_ReturnsDouble()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-avg-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        await _client.CreateDocumentAsync(builder.WithId("a").WithField("score", 10L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("b").WithField("score", 20L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("c").WithField("score", 30L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = builder.BuildAggregationQueryRequest(q =>
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Avg = new StructuredAggregationQuery.Types.Aggregation.Types.Avg
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" }
                },
                Alias = "avg_score"
            }));

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var value = Assert.Single(responses).Result.AggregateFields["avg_score"];
        Assert.Equal(Value.ValueTypeOneofCase.DoubleValue, value.ValueTypeCase);
        Assert.Equal(20.0, value.DoubleValue);
    }

    [Fact]
    public async Task Avg_EmptySet_ReturnsNull()
    {
        var col = $"agg-avg-empty-{Guid.NewGuid():N}";
        var builder = new DocumentBuilder().WithCollection(col);

        var request = builder.BuildAggregationQueryRequest(q =>
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Avg = new StructuredAggregationQuery.Types.Aggregation.Types.Avg
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" }
                },
                Alias = "avg_score"
            }));

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var value = Assert.Single(responses).Result.AggregateFields["avg_score"];
        Assert.Equal(Value.ValueTypeOneofCase.NullValue, value.ValueTypeCase);
    }

    [Fact]
    public async Task Avg_NonNumericValuesSkipped()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-avg-skip-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        await _client.CreateDocumentAsync(builder.WithId("a").WithField("score", 10L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("b").WithField("score", 20L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("c").WithField("score", "ignored").BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = builder.BuildAggregationQueryRequest(q =>
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Avg = new StructuredAggregationQuery.Types.Aggregation.Types.Avg
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" }
                },
                Alias = "avg_score"
            }));

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var value = Assert.Single(responses).Result.AggregateFields["avg_score"];
        Assert.Equal(Value.ValueTypeOneofCase.DoubleValue, value.ValueTypeCase);
        Assert.Equal(15.0, value.DoubleValue);
    }

    [Fact]
    public async Task Avg_NaN_ReturnsNaN()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-avg-nan-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        await _client.CreateDocumentAsync(builder.WithId("a").WithField("score", 10L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("b").WithField("score", double.NaN).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = builder.BuildAggregationQueryRequest(q =>
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Avg = new StructuredAggregationQuery.Types.Aggregation.Types.Avg
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" }
                },
                Alias = "avg_score"
            }));

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var value = Assert.Single(responses).Result.AggregateFields["avg_score"];
        Assert.Equal(Value.ValueTypeOneofCase.DoubleValue, value.ValueTypeCase);
        Assert.True(double.IsNaN(value.DoubleValue));
    }

    // ── Aliases ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Aliases_AutoAssigned_WhenNotProvided()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-alias-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        await _client.CreateDocumentAsync(builder.WithId("a").WithField("score", 5L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        // Two aggregations without aliases — should get field_0 and field_1
        var request = builder.BuildAggregationQueryRequest(q =>
        {
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Count = new StructuredAggregationQuery.Types.Aggregation.Types.Count()
                // no alias
            });
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Sum = new StructuredAggregationQuery.Types.Aggregation.Types.Sum
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" }
                }
                // no alias
            });
        });

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var fields = Assert.Single(responses).Result.AggregateFields;
        Assert.True(fields.ContainsKey("field_0"), "Expected auto-alias field_0");
        Assert.True(fields.ContainsKey("field_1"), "Expected auto-alias field_1");
    }

    [Fact]
    public async Task Aliases_AutoAssign_SkipsExplicitAliases_InCounter()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-alias-skip-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        await _client.CreateDocumentAsync(builder.WithId("a").WithField("x", 1L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        // Explicit alias, then no alias, then explicit, then no alias
        // → auto-counter increments only for the un-aliased ones: field_0, field_1
        var request = builder.BuildAggregationQueryRequest(q =>
        {
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Count = new StructuredAggregationQuery.Types.Aggregation.Types.Count(),
                Alias = "named_count"
            });
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Count = new StructuredAggregationQuery.Types.Aggregation.Types.Count()
                // no alias → field_0
            });
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Sum = new StructuredAggregationQuery.Types.Aggregation.Types.Sum
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "x" }
                },
                Alias = "named_sum"
            });
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Avg = new StructuredAggregationQuery.Types.Aggregation.Types.Avg
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "x" }
                }
                // no alias → field_1
            });
        });

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var fields = Assert.Single(responses).Result.AggregateFields;
        Assert.True(fields.ContainsKey("named_count"));
        Assert.True(fields.ContainsKey("field_0"));
        Assert.True(fields.ContainsKey("named_sum"));
        Assert.True(fields.ContainsKey("field_1"));
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TooManyAggregations_ReturnsInvalidArgument()
    {
        var builder = new DocumentBuilder().WithCollection("agg-validation");

        var request = builder.BuildAggregationQueryRequest(q =>
        {
            for (var i = 0; i < 6; i++)
            {
                q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
                {
                    Count = new StructuredAggregationQuery.Types.Aggregation.Types.Count()
                });
            }
        });

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);
        });

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    // ── Multiple aggregations in one request ─────────────────────────────────

    [Fact]
    public async Task MultipleAggregations_ReturnedInSingleResponse()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-multi-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        await _client.CreateDocumentAsync(builder.WithId("a").WithField("score", 10L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("b").WithField("score", 30L).BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = builder.BuildAggregationQueryRequest(q =>
        {
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Count = new StructuredAggregationQuery.Types.Aggregation.Types.Count(),
                Alias = "count"
            });
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Sum = new StructuredAggregationQuery.Types.Aggregation.Types.Sum
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" }
                },
                Alias = "sum"
            });
            q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Avg = new StructuredAggregationQuery.Types.Aggregation.Types.Avg
                {
                    Field = new StructuredQuery.Types.FieldReference { FieldPath = "score" }
                },
                Alias = "avg"
            });
        });

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        var result = Assert.Single(responses).Result;
        Assert.Equal(2L, result.AggregateFields["count"].IntegerValue);
        Assert.Equal(40L, result.AggregateFields["sum"].IntegerValue);
        Assert.Equal(20.0, result.AggregateFields["avg"].DoubleValue);
    }

    // ── Transaction protocol ──────────────────────────────────────────────────

    [Fact]
    public async Task NewTransaction_FirstResponseHasTransactionId_SecondHasResult()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-newtxn-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        await _client.CreateDocumentAsync(builder.WithId("a").BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = builder.BuildAggregationQueryRequest(
            q => q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Count = new StructuredAggregationQuery.Types.Aggregation.Types.Count(),
                Alias = "total"
            }),
            newTransaction: new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() });

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        Assert.Equal(2, responses.Count);

        // First response: transaction ID, no result
        var first = responses[0];
        Assert.False(first.Transaction.IsEmpty, "First response should contain the new transaction ID");
        Assert.Null(first.Result);
        Assert.NotNull(first.ReadTime);

        // Second response: result, no transaction ID
        var second = responses[1];
        Assert.True(second.Transaction.IsEmpty, "Second response should not contain a transaction ID");
        Assert.NotNull(second.Result);
        Assert.Equal(1L, second.Result.AggregateFields["total"].IntegerValue);
        Assert.NotNull(second.ReadTime);
    }

    [Fact]
    public async Task NewTransaction_ReturnedIdIsUsableForFollowUpRead()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-newtxn-followup-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col).WithId("doc1").WithField("val", 42L);

        await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        // Start aggregation with new_transaction
        var request = builder.BuildAggregationQueryRequest(
            q => q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Count = new StructuredAggregationQuery.Types.Aggregation.Types.Count(),
                Alias = "total"
            }),
            newTransaction: new TransactionOptions { ReadOnly = new TransactionOptions.Types.ReadOnly() });

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);
        var transactionId = responses[0].Transaction;
        Assert.False(transactionId.IsEmpty);

        // Use the transaction ID for a follow-up GetDocument
        var getRequest = new GetDocumentRequest
        {
            Name = builder.ExpectedName,
            Transaction = transactionId
        };
        var doc = await _client.GetDocumentAsync(getRequest, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(42L, doc.Fields["val"].IntegerValue);

        // Clean up: rollback the read-only transaction
        await _client.RollbackAsync(builder.BuildRollbackRequest(transactionId), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExistingTransaction_SingleResponseWithResult()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-txn-existing-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col);

        await _client.CreateDocumentAsync(builder.WithId("a").BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);
        await _client.CreateDocumentAsync(builder.WithId("b").BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        // Begin a transaction explicitly
        var txn = await _client.BeginTransactionAsync(builder.BuildBeginTransactionRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = builder.BuildAggregationQueryRequest(
            q => q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Count = new StructuredAggregationQuery.Types.Aggregation.Types.Count(),
                Alias = "total"
            }),
            transaction: txn.Transaction);

        var responses = await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        // Single response — no first-response protocol for existing transactions
        var response = Assert.Single(responses);
        Assert.True(response.Transaction.IsEmpty, "No transaction ID echoed for existing transaction");
        Assert.NotNull(response.Result);
        Assert.Equal(2L, response.Result.AggregateFields["total"].IntegerValue);

        // Rollback to clean up
        await _client.RollbackAsync(builder.BuildRollbackRequest(txn.Transaction), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExistingTransaction_ConflictDetected_OnCommit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var col = $"agg-txn-conflict-{suffix}";
        var builder = new DocumentBuilder().WithCollection(col).WithId("doc1").WithField("score", 10L);

        await _client.CreateDocumentAsync(builder.BuildCreateRequest(), cancellationToken: TestContext.Current.CancellationToken);

        // Begin a read-write transaction and run an aggregation inside it
        var txn = await _client.BeginTransactionAsync(builder.BuildBeginTransactionRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var request = builder.BuildAggregationQueryRequest(
            q => q.Aggregations.Add(new StructuredAggregationQuery.Types.Aggregation
            {
                Count = new StructuredAggregationQuery.Types.Aggregation.Types.Count(),
                Alias = "total"
            }),
            transaction: txn.Transaction);

        await RunAggregationAsync(_client, request, TestContext.Current.CancellationToken);

        // Concurrently delete the document — mutates the subtree the transaction read
        await _client.DeleteDocumentAsync(builder.BuildDeleteRequest(), cancellationToken: TestContext.Current.CancellationToken);

        // Committing the transaction should now be aborted due to conflict
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            _client.CommitAsync(builder.BuildTransactionalCommitRequest(txn.Transaction), cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        Assert.Equal(StatusCode.Aborted, ex.StatusCode);
    }
}
