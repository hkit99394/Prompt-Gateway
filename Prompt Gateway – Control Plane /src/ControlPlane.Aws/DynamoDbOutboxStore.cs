using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ControlPlane.Core;

namespace ControlPlane.Aws;

public sealed class DynamoDbOutboxStore : DynamoDbStoreBase, IOutboxStore
{
    private const string PartitionKey = "pk";
    private const string SortKey = "sk";
    private const string JobListPartitionKeyField = "gsi1pk";
    private const string JobListSortKeyField = "gsi1sk";
    private const string StatusField = "status";
    private const string MessageField = "message_json";
    private const string CreatedAtField = "created_at";
    private const string ProcessingStartedAtField = "processing_started_at";
    private const string ProcessingOwnerField = "processing_owner";
    private const string ErrorField = "error";
    private const string TtlField = "ttl";
    private const string OutboxPartition = "OUTBOX";
    private const string OutboxReadyPartition = "OUTBOX_READY";
    private const string OutboxProcessingPartition = "OUTBOX_PROCESSING";
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(1);
    private readonly IClock _clock;
    private readonly string _processingOwner = $"worker-{Guid.NewGuid():N}";

    public DynamoDbOutboxStore(IAmazonDynamoDB dynamoDb, DynamoDbOptions options, IClock clock)
        : base(dynamoDb, options)
    {
        _clock = clock;
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
                [JobListPartitionKeyField] = Attr(OutboxReadyPartition),
                [JobListSortKeyField] = Attr(FormatTimestamp(message.CreatedAt)),
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
        var pending = await TryClaimNextAsync(
            OutboxReadyPartition,
            leaseCutoff: null,
            cancellationToken);
        if (pending is not null)
        {
            return pending;
        }

        return await TryClaimNextAsync(
            OutboxProcessingPartition,
            leaseCutoff: FormatTimestamp(_clock.UtcNow.Subtract(ProcessingLease)),
            cancellationToken);
    }

    public async Task MarkDispatchedAsync(string outboxId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var ttl = ToUnixTimeSeconds(_clock.UtcNow.AddDays(Options.OutboxTerminalTtlDays));
        var request = new UpdateItemRequest
        {
            TableName = Options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr(OutboxPartition),
                [SortKey] = Attr(outboxId)
            },
            UpdateExpression = "SET #status = :dispatched, #ttl = :ttl REMOVE #startedAt, #owner, #gsiPk, #gsiSk",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = StatusField,
                ["#startedAt"] = ProcessingStartedAtField,
                ["#owner"] = ProcessingOwnerField,
                ["#gsiPk"] = JobListPartitionKeyField,
                ["#gsiSk"] = JobListSortKeyField,
                ["#ttl"] = TtlField
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":dispatched"] = Attr("dispatched"),
                [":ttl"] = Attr(ttl)
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
            UpdateExpression = "SET #status = :pending, #gsiPk = :readyPartition, #gsiSk = #createdAt REMOVE #startedAt, #owner, #error, #ttl",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = StatusField,
                ["#startedAt"] = ProcessingStartedAtField,
                ["#owner"] = ProcessingOwnerField,
                ["#error"] = ErrorField,
                ["#gsiPk"] = JobListPartitionKeyField,
                ["#gsiSk"] = JobListSortKeyField,
                ["#createdAt"] = CreatedAtField,
                ["#ttl"] = TtlField
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pending"] = Attr("pending"),
                [":readyPartition"] = Attr(OutboxReadyPartition)
            }
        };

        await DynamoDb.UpdateItemAsync(request, cancellationToken);
    }

    public async Task MarkFailedAsync(string outboxId, string reason, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var ttl = ToUnixTimeSeconds(_clock.UtcNow.AddDays(Options.OutboxTerminalTtlDays));
        var request = new UpdateItemRequest
        {
            TableName = Options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr(OutboxPartition),
                [SortKey] = Attr(outboxId)
            },
            UpdateExpression = "SET #status = :failed, #error = :error, #ttl = :ttl REMOVE #startedAt, #owner, #gsiPk, #gsiSk",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = StatusField,
                ["#error"] = ErrorField,
                ["#startedAt"] = ProcessingStartedAtField,
                ["#owner"] = ProcessingOwnerField,
                ["#gsiPk"] = JobListPartitionKeyField,
                ["#gsiSk"] = JobListSortKeyField,
                ["#ttl"] = TtlField
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":failed"] = Attr("failed"),
                [":error"] = Attr(reason),
                [":ttl"] = Attr(ttl)
            }
        };

        await DynamoDb.UpdateItemAsync(request, cancellationToken);
    }

    private async Task<OutboxDispatchMessage?> TryClaimNextAsync(
        string queuePartition,
        string? leaseCutoff,
        CancellationToken cancellationToken)
    {
        Dictionary<string, AttributeValue>? startKey = null;

        while (true)
        {
            var request = BuildQueryRequest(queuePartition, leaseCutoff, startKey);
            var response = await DynamoDb.QueryAsync(request, cancellationToken);

            foreach (var item in response.Items)
            {
                var outboxId = GetString(item, SortKey);
                if (string.IsNullOrWhiteSpace(outboxId))
                {
                    continue;
                }

                var claimed = await TryClaimAsync(outboxId, queuePartition, leaseCutoff, cancellationToken);
                if (!claimed)
                {
                    continue;
                }

                var payload = GetString(item, MessageField);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    await MarkFailedAsync(outboxId, "missing_payload", cancellationToken);
                    continue;
                }

                return JsonSerializer.Deserialize<OutboxDispatchMessage>(payload, SerializerOptions);
            }

            startKey = response.LastEvaluatedKey;
            if (startKey is null || startKey.Count == 0)
            {
                return null;
            }
        }
    }

    private QueryRequest BuildQueryRequest(
        string queuePartition,
        string? leaseCutoff,
        Dictionary<string, AttributeValue>? startKey)
    {
        var expressionAttributeNames = new Dictionary<string, string>
        {
            ["#gsiPk"] = JobListPartitionKeyField
        };

        var request = new QueryRequest
        {
            TableName = Options.TableName,
            IndexName = Options.JobListIndexName,
            KeyConditionExpression = leaseCutoff is null
                ? "#gsiPk = :gsiPk"
                : "#gsiPk = :gsiPk AND #gsiSk <= :cutoff",
            ExpressionAttributeNames = expressionAttributeNames,
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":gsiPk"] = Attr(queuePartition)
            },
            Limit = 25,
            ScanIndexForward = true,
            ExclusiveStartKey = startKey
        };

        if (leaseCutoff is not null)
        {
            expressionAttributeNames["#gsiSk"] = JobListSortKeyField;
            request.ExpressionAttributeValues[":cutoff"] = Attr(leaseCutoff);
        }

        return request;
    }

    private async Task<bool> TryClaimAsync(
        string outboxId,
        string queuePartition,
        string? leaseCutoff,
        CancellationToken cancellationToken)
    {
        var now = FormatTimestamp(_clock.UtcNow);

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
                UpdateExpression = "SET #status = :processing, #startedAt = :now, #owner = :owner, #gsiPk = :processingPartition, #gsiSk = :now REMOVE #error",
                ConditionExpression = queuePartition == OutboxReadyPartition
                    ? "#status = :pending AND #gsiPk = :readyPartition"
                    : "#status = :processing AND #gsiPk = :processingPartition AND (#startedAt < :cutoff OR attribute_not_exists(#startedAt))",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#status"] = StatusField,
                    ["#startedAt"] = ProcessingStartedAtField,
                    ["#owner"] = ProcessingOwnerField,
                    ["#error"] = ErrorField,
                    ["#gsiPk"] = JobListPartitionKeyField,
                    ["#gsiSk"] = JobListSortKeyField
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pending"] = Attr("pending"),
                    [":processing"] = Attr("processing"),
                    [":readyPartition"] = Attr(OutboxReadyPartition),
                    [":processingPartition"] = Attr(OutboxProcessingPartition),
                    [":now"] = Attr(now),
                    [":owner"] = Attr(_processingOwner)
                }.WithOptionalValue(":cutoff", leaseCutoff is null ? null : Attr(leaseCutoff))
            }, cancellationToken);

            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }
}

internal static class DynamoDbAttributeValueDictionaryExtensions
{
    public static Dictionary<string, AttributeValue> WithOptionalValue(
        this Dictionary<string, AttributeValue> source,
        string key,
        AttributeValue? value)
    {
        if (value is not null)
        {
            source[key] = value;
        }

        return source;
    }
}
