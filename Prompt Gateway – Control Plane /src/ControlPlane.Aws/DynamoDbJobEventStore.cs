using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ControlPlane.Core;

namespace ControlPlane.Aws;

public sealed class DynamoDbJobEventStore : DynamoDbStoreBase, IJobEventStore
{
    private const string PartitionKey = "pk";
    private const string SortKey = "sk";
    private const string EventField = "event_json";
    private const string EventPrefix = "EVENT#";
    private const string TtlField = "ttl";

    public DynamoDbJobEventStore(IAmazonDynamoDB dynamoDb, DynamoDbOptions options)
        : base(dynamoDb, options)
    {
    }

    public async Task AppendAsync(JobEvent jobEvent, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var sortKey = $"{EventPrefix}{FormatTimestamp(jobEvent.OccurredAt)}#{jobEvent.AttemptId}#{jobEvent.Type}";
        var request = new PutItemRequest
        {
            TableName = Options.TableName,
            Item = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr($"JOB#{jobEvent.JobId}"),
                [SortKey] = Attr(sortKey),
                [EventField] = Attr(JsonSerializer.Serialize(jobEvent, SerializerOptions))
            }
        };

        if (Options.EventTtlDays > 0)
        {
            request.Item[TtlField] = Attr(ToUnixTimeSeconds(jobEvent.OccurredAt.AddDays(Options.EventTtlDays)));
        }

        await DynamoDb.PutItemAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<JobEvent>> GetAsync(string jobId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var events = new List<JobEvent>();
        Dictionary<string, AttributeValue>? startKey = null;

        while (true)
        {
            var request = new QueryRequest
            {
                TableName = Options.TableName,
                KeyConditionExpression = "#pk = :pk AND begins_with(#sk, :prefix)",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#pk"] = PartitionKey,
                    ["#sk"] = SortKey
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = Attr($"JOB#{jobId}"),
                    [":prefix"] = Attr(EventPrefix)
                },
                ScanIndexForward = true,
                ExclusiveStartKey = startKey
            };

            var response = await DynamoDb.QueryAsync(request, cancellationToken);
            foreach (var item in response.Items)
            {
                var payload = GetString(item, EventField);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    continue;
                }

                var jobEvent = JsonSerializer.Deserialize<JobEvent>(payload, SerializerOptions);
                if (jobEvent is not null)
                {
                    events.Add(jobEvent);
                }
            }

            startKey = response.LastEvaluatedKey;
            if (startKey is null || startKey.Count == 0)
            {
                break;
            }
        }

        return events;
    }
}
