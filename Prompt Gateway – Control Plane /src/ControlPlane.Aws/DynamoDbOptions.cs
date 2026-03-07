namespace ControlPlane.Aws;

public sealed class DynamoDbOptions
{
    public string TableName { get; init; } = string.Empty;
    public string JobListIndexName { get; init; } = "gsi1";
}
