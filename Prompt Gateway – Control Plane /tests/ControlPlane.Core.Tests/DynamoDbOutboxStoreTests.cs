using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ControlPlane.Aws;
using ControlPlane.Core;
using NSubstitute;

namespace ControlPlane.Core.Tests;

public class DynamoDbOutboxStoreTests
{
    [Test]
    public async Task TryDequeueAsync_Paginates_WhenFirstPageHasNoRunnableItems()
    {
        var dynamoDb = Substitute.For<IAmazonDynamoDB>();
        var store = new DynamoDbOutboxStore(dynamoDb, new DynamoDbOptions { TableName = "test-table" });

        var message = BuildOutboxMessage("outbox-2");
        var payload = SerializeOutboxMessage(message);
        var pageToken = new Dictionary<string, AttributeValue>
        {
            ["pk"] = new() { S = "OUTBOX" },
            ["sk"] = new() { S = "outbox-1" }
        };

        dynamoDb.QueryAsync(Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new QueryResponse
                {
                    Items = new List<Dictionary<string, AttributeValue>>(),
                    LastEvaluatedKey = pageToken
                }),
                Task.FromResult(new QueryResponse
                {
                    Items = new List<Dictionary<string, AttributeValue>>
                    {
                        new()
                        {
                            ["pk"] = new() { S = "OUTBOX" },
                            ["sk"] = new() { S = message.OutboxId },
                            ["message_json"] = new() { S = payload }
                        }
                    },
                    LastEvaluatedKey = new Dictionary<string, AttributeValue>()
                }));

        dynamoDb.UpdateItemAsync(Arg.Any<UpdateItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new UpdateItemResponse()));

        var dequeued = await store.TryDequeueAsync(CancellationToken.None);

        Assert.That(dequeued, Is.Not.Null);
        Assert.That(dequeued!.OutboxId, Is.EqualTo("outbox-2"));
        await dynamoDb.Received(1).QueryAsync(
            Arg.Is<QueryRequest>(request => request.ExclusiveStartKey != null && request.ExclusiveStartKey.Count > 0),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryDequeueAsync_Continues_WhenFirstClaimFailsConditionCheck()
    {
        var dynamoDb = Substitute.For<IAmazonDynamoDB>();
        var store = new DynamoDbOutboxStore(dynamoDb, new DynamoDbOptions { TableName = "test-table" });

        var firstMessage = BuildOutboxMessage("outbox-1");
        var secondMessage = BuildOutboxMessage("outbox-2");

        dynamoDb.QueryAsync(Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new QueryResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>
                {
                    new()
                    {
                        ["pk"] = new() { S = "OUTBOX" },
                        ["sk"] = new() { S = firstMessage.OutboxId },
                        ["message_json"] = new() { S = SerializeOutboxMessage(firstMessage) }
                    },
                    new()
                    {
                        ["pk"] = new() { S = "OUTBOX" },
                        ["sk"] = new() { S = secondMessage.OutboxId },
                        ["message_json"] = new() { S = SerializeOutboxMessage(secondMessage) }
                    }
                },
                LastEvaluatedKey = new Dictionary<string, AttributeValue>()
            }));

        dynamoDb.UpdateItemAsync(
                Arg.Is<UpdateItemRequest>(request => request.Key["sk"].S == "outbox-1"),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<UpdateItemResponse>(new ConditionalCheckFailedException("claim-lost")));

        dynamoDb.UpdateItemAsync(
                Arg.Is<UpdateItemRequest>(request => request.Key["sk"].S == "outbox-2"),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new UpdateItemResponse()));

        var dequeued = await store.TryDequeueAsync(CancellationToken.None);

        Assert.That(dequeued, Is.Not.Null);
        Assert.That(dequeued!.OutboxId, Is.EqualTo("outbox-2"));
        await dynamoDb.Received(2).UpdateItemAsync(Arg.Any<UpdateItemRequest>(), Arg.Any<CancellationToken>());
    }

    private static OutboxDispatchMessage BuildOutboxMessage(string outboxId)
    {
        return new OutboxDispatchMessage(
            outboxId,
            new DispatchMessage
            {
                JobId = "job-1",
                AttemptId = "attempt-1",
                TraceId = "trace-1",
                Provider = "openai",
                Model = "gpt-4.1",
                IdempotencyKey = "job-1:attempt-1",
                Request = new CanonicalJobRequest
                {
                    JobId = "job-1",
                    AttemptId = "attempt-1",
                    TraceId = "trace-1",
                    TaskType = "chat_completion"
                }
            },
            DateTimeOffset.UtcNow);
    }

    private static string SerializeOutboxMessage(OutboxDispatchMessage message)
    {
        return JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
