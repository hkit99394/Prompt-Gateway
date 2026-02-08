using System.Globalization;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace ControlPlane.Aws;

internal abstract class DynamoDbStoreBase
{
    protected readonly IAmazonDynamoDB DynamoDb;
    protected readonly DynamoDbOptions Options;
    protected readonly JsonSerializerOptions SerializerOptions;

    protected DynamoDbStoreBase(IAmazonDynamoDB dynamoDb, DynamoDbOptions options)
    {
        DynamoDb = dynamoDb;
        Options = options;
        SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    protected void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(Options.TableName))
        {
            throw new InvalidOperationException("DynamoDbOptions.TableName is not configured.");
        }
    }

    protected static string FormatTimestamp(DateTimeOffset value)
        => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    protected static DateTimeOffset ParseTimestamp(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    protected static AttributeValue Attr(string value) => new() { S = value };

    protected static string? GetString(Dictionary<string, AttributeValue> item, string key)
        => item.TryGetValue(key, out var value) ? value.S : null;
}
