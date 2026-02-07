namespace Provider.Worker.Services;

public sealed class QueueMessage
{
    public string? Body { get; set; }
    public string? ReceiptHandle { get; set; }
}

public sealed class QueueReceiveRequest
{
    public string QueueUrl { get; set; } = string.Empty;
    public int MaxNumberOfMessages { get; set; }
    public int WaitTimeSeconds { get; set; }
    public int VisibilityTimeoutSeconds { get; set; }
}

public sealed class QueueReceiveResult
{
    public List<QueueMessage> Messages { get; set; } = new();
}
