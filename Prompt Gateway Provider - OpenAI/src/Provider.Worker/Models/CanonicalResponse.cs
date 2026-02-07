using System.Text.Json.Serialization;

namespace Provider.Worker.Models;

public class CanonicalResponse
{
    [JsonPropertyName("output_text")]
    public string? OutputText { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }

    [JsonPropertyName("usage")]
    public UsageMetrics? Usage { get; set; }

    [JsonPropertyName("raw_payload_ref")]
    public string? RawPayloadReference { get; set; }
}
