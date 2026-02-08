using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ControlPlane.Core;

namespace ControlPlane.Aws;

public sealed class DynamoDbJobStore : DynamoDbStoreBase, IJobStore
{
    private const string PartitionKey = "pk";
    private const string SortKey = "sk";
    private const string SnapshotField = "job_snapshot";
    private const string UpdatedAtField = "updated_at";
    private const string JobPartitionPrefix = "JOB#";
    private const string JobSortKey = "JOB";

    public DynamoDbJobStore(IAmazonDynamoDB dynamoDb, DynamoDbOptions options)
        : base(dynamoDb, options)
    {
    }

    public async Task CreateAsync(JobRecord job, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var snapshot = ToSnapshot(job);
        var request = new PutItemRequest
        {
            TableName = Options.TableName,
            Item = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr($"{JobPartitionPrefix}{job.JobId}"),
                [SortKey] = Attr(JobSortKey),
                [SnapshotField] = Attr(JsonSerializer.Serialize(snapshot, SerializerOptions)),
                [UpdatedAtField] = Attr(FormatTimestamp(job.UpdatedAt))
            },
            ConditionExpression = "attribute_not_exists(#pk)",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#pk"] = PartitionKey
            }
        };

        await DynamoDb.PutItemAsync(request, cancellationToken);
    }

    public async Task<JobRecord?> GetAsync(string jobId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var response = await DynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = Options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr($"{JobPartitionPrefix}{jobId}"),
                [SortKey] = Attr(JobSortKey)
            }
        }, cancellationToken);

        if (response.Item is null || response.Item.Count == 0)
        {
            return null;
        }

        var snapshotJson = GetString(response.Item, SnapshotField);
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return null;
        }

        var snapshot = JsonSerializer.Deserialize<JobRecordSnapshot>(snapshotJson, SerializerOptions);
        return snapshot is null ? null : JobRecord.Restore(snapshot);
    }

    public async Task UpdateAsync(JobRecord job, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var snapshot = ToSnapshot(job);
        var request = new UpdateItemRequest
        {
            TableName = Options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr($"{JobPartitionPrefix}{job.JobId}"),
                [SortKey] = Attr(JobSortKey)
            },
            UpdateExpression = "SET #snapshot = :snapshot, #updatedAt = :updatedAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#snapshot"] = SnapshotField,
                ["#updatedAt"] = UpdatedAtField
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":snapshot"] = Attr(JsonSerializer.Serialize(snapshot, SerializerOptions)),
                [":updatedAt"] = Attr(FormatTimestamp(job.UpdatedAt))
            }
        };

        await DynamoDb.UpdateItemAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<JobSummary>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (limit <= 0)
        {
            return Array.Empty<JobSummary>();
        }

        var results = new List<JobSummary>(limit);
        Dictionary<string, AttributeValue>? startKey = null;

        while (results.Count < limit)
        {
            var pageLimit = Math.Min(100, limit - results.Count);
            var request = new ScanRequest
            {
                TableName = Options.TableName,
                FilterExpression = "begins_with(#pk, :prefix) AND #sk = :sk",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#pk"] = PartitionKey,
                    ["#sk"] = SortKey
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":prefix"] = Attr(JobPartitionPrefix),
                    [":sk"] = Attr(JobSortKey)
                },
                Limit = pageLimit,
                ExclusiveStartKey = startKey,
                ProjectionExpression = "#pk, #sk, #snapshot, #updatedAt"
            };

            request.ExpressionAttributeNames["#snapshot"] = SnapshotField;
            request.ExpressionAttributeNames["#updatedAt"] = UpdatedAtField;

            var response = await DynamoDb.ScanAsync(request, cancellationToken);
            foreach (var item in response.Items)
            {
                var snapshotJson = GetString(item, SnapshotField);
                if (string.IsNullOrWhiteSpace(snapshotJson))
                {
                    continue;
                }

                var snapshot = JsonSerializer.Deserialize<JobRecordSnapshot>(snapshotJson, SerializerOptions);
                if (snapshot is null)
                {
                    continue;
                }

                results.Add(new JobSummary(
                    snapshot.JobId,
                    snapshot.TraceId,
                    snapshot.CurrentAttemptId,
                    snapshot.State,
                    snapshot.CreatedAt,
                    snapshot.UpdatedAt));
            }

            startKey = response.LastEvaluatedKey;
            if (startKey is null || startKey.Count == 0)
            {
                break;
            }
        }

        return results
            .OrderByDescending(summary => summary.UpdatedAt)
            .Take(limit)
            .ToList();
    }

    private static JobRecordSnapshot ToSnapshot(JobRecord job)
    {
        var attempts = job.Attempts.Select(attempt => new JobAttemptSnapshot(
            attempt.AttemptId,
            attempt.State,
            attempt.Provider,
            attempt.Model,
            attempt.RoutingDecision,
            attempt.CreatedAt,
            attempt.UpdatedAt)).ToList();

        return new JobRecordSnapshot(
            job.JobId,
            job.TraceId,
            job.State,
            job.CreatedAt,
            job.UpdatedAt,
            job.CurrentAttemptId,
            job.Request,
            attempts);
    }
}
