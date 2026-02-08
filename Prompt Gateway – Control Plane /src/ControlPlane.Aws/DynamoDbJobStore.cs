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
                [PartitionKey] = Attr($"JOB#{job.JobId}"),
                [SortKey] = Attr("JOB"),
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
                [PartitionKey] = Attr($"JOB#{jobId}"),
                [SortKey] = Attr("JOB")
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
                [PartitionKey] = Attr($"JOB#{job.JobId}"),
                [SortKey] = Attr("JOB")
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
