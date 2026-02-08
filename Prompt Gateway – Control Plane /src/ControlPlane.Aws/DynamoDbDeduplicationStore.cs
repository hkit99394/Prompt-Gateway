using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ControlPlane.Core;

namespace ControlPlane.Aws;

public sealed class DynamoDbDeduplicationStore : DynamoDbStoreBase, IDeduplicationStore
{
    private const string PartitionKey = "pk";
    private const string SortKey = "sk";
    private const string StatusField = "status";

    public DynamoDbDeduplicationStore(IAmazonDynamoDB dynamoDb, DynamoDbOptions options)
        : base(dynamoDb, options)
    {
    }

    public async Task<bool> TryStartAsync(string jobId, string attemptId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var request = new PutItemRequest
        {
            TableName = Options.TableName,
            Item = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr($"DEDUP#{jobId}"),
                [SortKey] = Attr($"ATTEMPT#{attemptId}"),
                [StatusField] = Attr("processing")
            },
            ConditionExpression = "attribute_not_exists(#pk) AND attribute_not_exists(#sk)",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#pk"] = PartitionKey,
                ["#sk"] = SortKey
            }
        };

        try
        {
            await DynamoDb.PutItemAsync(request, cancellationToken);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task MarkCompletedAsync(string jobId, string attemptId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var request = new UpdateItemRequest
        {
            TableName = Options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr($"DEDUP#{jobId}"),
                [SortKey] = Attr($"ATTEMPT#{attemptId}")
            },
            UpdateExpression = "SET #status = :completed",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = StatusField
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":completed"] = Attr("completed")
            }
        };

        await DynamoDb.UpdateItemAsync(request, cancellationToken);
    }
}
