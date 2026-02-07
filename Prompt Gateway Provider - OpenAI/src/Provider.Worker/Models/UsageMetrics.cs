using System.Text.Json.Serialization;

namespace Provider.Worker.Models;

public class UsageMetrics
{
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int? TotalTokens { get; set; }

    [JsonPropertyName("estimated_cost_usd")]
    public decimal? EstimatedCostUsd { get; set; }
}
