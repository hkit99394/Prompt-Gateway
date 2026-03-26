using System.Text.Json.Serialization;

namespace Provider.Worker.Lambda;

public sealed class SqsEvent
{
    [JsonPropertyName("Records")]
    public List<SqsMessage> Records { get; set; } = [];
}

public sealed class SqsMessage
{
    [JsonPropertyName("messageId")]
    public string? MessageId { get; set; }

    [JsonPropertyName("receiptHandle")]
    public string? ReceiptHandle { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }
}

public sealed class SqsBatchResponse
{
    [JsonPropertyName("batchItemFailures")]
    public List<BatchItemFailure> BatchItemFailures { get; set; } = [];

    public sealed class BatchItemFailure
    {
        [JsonPropertyName("itemIdentifier")]
        public string ItemIdentifier { get; set; } = string.Empty;
    }
}
