using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ControlPlane.Core;

namespace ControlPlane.Aws;

public sealed class DynamoDbResultStore : DynamoDbStoreBase, IResultStore
{
    private const string PartitionKey = "pk";
    private const string SortKey = "sk";
    private const string ResponseField = "response_json";

    public DynamoDbResultStore(IAmazonDynamoDB dynamoDb, DynamoDbOptions options)
        : base(dynamoDb, options)
    {
    }

    public async Task SaveAttemptResultAsync(
        string jobId,
        string attemptId,
        CanonicalResponse response,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await PutResultAsync(jobId, $"RESULT#ATTEMPT#{attemptId}", response, cancellationToken);
    }

    public async Task SaveFinalResultAsync(string jobId, CanonicalResponse response, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await PutResultAsync(jobId, "RESULT#FINAL", response, cancellationToken);
    }

    public async Task<CanonicalResponse?> GetFinalResultAsync(string jobId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var response = await DynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = Options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr($"JOB#{jobId}"),
                [SortKey] = Attr("RESULT#FINAL")
            }
        }, cancellationToken);

        if (response.Item is null || response.Item.Count == 0)
        {
            return null;
        }

        var payload = GetString(response.Item, ResponseField);
        return string.IsNullOrWhiteSpace(payload)
            ? null
            : JsonSerializer.Deserialize<CanonicalResponse>(payload, SerializerOptions);
    }

    private async Task PutResultAsync(
        string jobId,
        string sortKey,
        CanonicalResponse response,
        CancellationToken cancellationToken)
    {
        var request = new PutItemRequest
        {
            TableName = Options.TableName,
            Item = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr($"JOB#{jobId}"),
                [SortKey] = Attr(sortKey),
                [ResponseField] = Attr(JsonSerializer.Serialize(response, SerializerOptions))
            }
        };

        await DynamoDb.PutItemAsync(request, cancellationToken);
    }
}
