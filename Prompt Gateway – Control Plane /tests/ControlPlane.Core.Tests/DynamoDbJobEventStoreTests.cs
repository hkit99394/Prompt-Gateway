using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ControlPlane.Aws;
using ControlPlane.Core;
using NSubstitute;

namespace ControlPlane.Core.Tests;

public class DynamoDbJobEventStoreTests
{
    [Test]
    public async Task GetAsync_PaginatesAcrossAllQueryPages()
    {
        var dynamoDb = Substitute.For<IAmazonDynamoDB>();
        var store = new DynamoDbJobEventStore(dynamoDb, new DynamoDbOptions { TableName = "test-table" });
        var first = JobEvent.Created("job-1", "attempt-1", new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero));
        var second = JobEvent.Failed(
            "job-1",
            "attempt-1",
            new DateTimeOffset(2026, 3, 7, 0, 1, 0, TimeSpan.Zero),
            new CanonicalError("provider_error", "boom"));

        var pageToken = new Dictionary<string, AttributeValue>
        {
            ["pk"] = new() { S = "JOB#job-1" },
            ["sk"] = new() { S = "EVENT#2026-03-07T00:00:00.0000000Z#attempt-1#Created" }
        };

        dynamoDb.QueryAsync(Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new QueryResponse
                {
                    Items = new List<Dictionary<string, AttributeValue>>
                    {
                        new()
                        {
                            ["event_json"] = new() { S = Serialize(first) }
                        }
                    },
                    LastEvaluatedKey = pageToken
                }),
                Task.FromResult(new QueryResponse
                {
                    Items = new List<Dictionary<string, AttributeValue>>
                    {
                        new()
                        {
                            ["event_json"] = new() { S = Serialize(second) }
                        }
                    },
                    LastEvaluatedKey = new Dictionary<string, AttributeValue>()
                }));

        var events = await store.GetAsync("job-1", CancellationToken.None);

        Assert.That(events, Has.Count.EqualTo(2));
        Assert.That(events[0].Type, Is.EqualTo(JobEventType.Created));
        Assert.That(events[1].Type, Is.EqualTo(JobEventType.Failed));
        await dynamoDb.Received(1).QueryAsync(
            Arg.Is<QueryRequest>(request => request.ExclusiveStartKey != null && request.ExclusiveStartKey.Count > 0),
            Arg.Any<CancellationToken>());
    }

    private static string Serialize(JobEvent jobEvent)
    {
        return JsonSerializer.Serialize(jobEvent, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
