using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ControlPlane.Core;

namespace ControlPlane.Aws;

public sealed class DynamoDbOutboxStore : DynamoDbStoreBase, IOutboxStore
{
    private const string PartitionKey = "pk";
    private const string SortKey = "sk";
    private const string StatusField = "status";
    private const string MessageField = "message_json";
    private const string CreatedAtField = "created_at";
    private const string ProcessingStartedAtField = "processing_started_at";
    private const string ProcessingOwnerField = "processing_owner";
    private const string ErrorField = "error";
    private const string OutboxPartition = "OUTBOX";
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(1);
    private readonly string _processingOwner = $"worker-{Guid.NewGuid():N}";

    public DynamoDbOutboxStore(IAmazonDynamoDB dynamoDb, DynamoDbOptions options)
        : base(dynamoDb, options)
    {
    }

    public async Task EnqueueDispatchAsync(OutboxDispatchMessage message, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var request = new PutItemRequest
        {
            TableName = Options.TableName,
            Item = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr(OutboxPartition),
                [SortKey] = Attr(message.OutboxId),
                [StatusField] = Attr("pending"),
                [CreatedAtField] = Attr(FormatTimestamp(message.CreatedAt)),
                [MessageField] = Attr(JsonSerializer.Serialize(message, SerializerOptions))
            },
            ConditionExpression = "attribute_not_exists(#pk) AND attribute_not_exists(#sk)",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#pk"] = PartitionKey,
                ["#sk"] = SortKey
            }
        };

        await DynamoDb.PutItemAsync(request, cancellationToken);
    }

    public async Task<OutboxDispatchMessage?> TryDequeueAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var cutoff = FormatTimestamp(DateTimeOffset.UtcNow.Subtract(ProcessingLease));
        var request = new QueryRequest
        {
            TableName = Options.TableName,
            KeyConditionExpression = "#pk = :pk",
            FilterExpression = "(#status = :pending) OR (#status = :processing AND (#startedAt < :cutoff OR attribute_not_exists(#startedAt)))",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#pk"] = PartitionKey,
                ["#status"] = StatusField,
                ["#startedAt"] = ProcessingStartedAtField
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = Attr(OutboxPartition),
                [":pending"] = Attr("pending"),
                [":processing"] = Attr("processing"),
                [":cutoff"] = Attr(cutoff)
            },
            Limit = 1,
            ScanIndexForward = true
        };

        var response = await DynamoDb.QueryAsync(request, cancellationToken);
        var item = response.Items.FirstOrDefault();
        if (item is null)
        {
            return null;
        }

        var outboxId = GetString(item, SortKey);
        if (string.IsNullOrWhiteSpace(outboxId))
        {
            return null;
        }

        var now = FormatTimestamp(DateTimeOffset.UtcNow);
        try
        {
            await DynamoDb.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = Options.TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    [PartitionKey] = Attr(OutboxPartition),
                    [SortKey] = Attr(outboxId)
                },
                UpdateExpression = "SET #status = :processing, #startedAt = :now, #owner = :owner REMOVE #error",
                ConditionExpression = "#status = :pending OR (#status = :processing AND (#startedAt < :cutoff OR attribute_not_exists(#startedAt)))",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#status"] = StatusField,
                    ["#startedAt"] = ProcessingStartedAtField,
                    ["#owner"] = ProcessingOwnerField,
                    ["#error"] = ErrorField
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pending"] = Attr("pending"),
                    [":processing"] = Attr("processing"),
                    [":cutoff"] = Attr(cutoff),
                    [":now"] = Attr(now),
                    [":owner"] = Attr(_processingOwner)
                }
            }, cancellationToken);
        }
        catch (ConditionalCheckFailedException)
        {
            return null;
        }

        var payload = GetString(item, MessageField);
        if (string.IsNullOrWhiteSpace(payload))
        {
            await MarkFailedAsync(outboxId, "missing_payload", cancellationToken);
            return null;
        }

        return JsonSerializer.Deserialize<OutboxDispatchMessage>(payload, SerializerOptions);
    }

    public async Task MarkDispatchedAsync(string outboxId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var request = new UpdateItemRequest
        {
            TableName = Options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr(OutboxPartition),
                [SortKey] = Attr(outboxId)
            },
            UpdateExpression = "SET #status = :dispatched REMOVE #startedAt, #owner",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = StatusField,
                ["#startedAt"] = ProcessingStartedAtField,
                ["#owner"] = ProcessingOwnerField
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":dispatched"] = Attr("dispatched")
            }
        };

        await DynamoDb.UpdateItemAsync(request, cancellationToken);
    }

    public async Task ReleaseAsync(string outboxId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var request = new UpdateItemRequest
        {
            TableName = Options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr(OutboxPartition),
                [SortKey] = Attr(outboxId)
            },
            UpdateExpression = "SET #status = :pending REMOVE #startedAt, #owner, #error",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = StatusField,
                ["#startedAt"] = ProcessingStartedAtField,
                ["#owner"] = ProcessingOwnerField,
                ["#error"] = ErrorField
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pending"] = Attr("pending")
            }
        };

        await DynamoDb.UpdateItemAsync(request, cancellationToken);
    }

    public async Task MarkFailedAsync(string outboxId, string reason, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var request = new UpdateItemRequest
        {
            TableName = Options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr(OutboxPartition),
                [SortKey] = Attr(outboxId)
            },
            UpdateExpression = "SET #status = :failed, #error = :error REMOVE #startedAt, #owner",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = StatusField,
                ["#error"] = ErrorField,
                ["#startedAt"] = ProcessingStartedAtField,
                ["#owner"] = ProcessingOwnerField
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":failed"] = Attr("failed"),
                [":error"] = Attr(reason)
            }
        };

        await DynamoDb.UpdateItemAsync(request, cancellationToken);
    }
}
