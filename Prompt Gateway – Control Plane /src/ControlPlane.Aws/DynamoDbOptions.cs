namespace ControlPlane.Aws;

public sealed class DynamoDbOptions
{
    public string TableName { get; init; } = string.Empty;
    public string JobListIndexName { get; init; } = "gsi1";
    public int DeduplicationTtlDays { get; init; } = 7;
    public int OutboxTerminalTtlDays { get; init; } = 7;
    public int EventTtlDays { get; init; } = 30;
    public int ResultTtlDays { get; init; } = 30;
}
