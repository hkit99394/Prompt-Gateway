using System.Text.Json.Serialization;

namespace Provider.Worker.Models;

public class UsageMetrics
{
    [JsonPropertyName("input_tokens")]
    public int? InputTokens { get; set; }

    [JsonPropertyName("input_tokens_details")]
    public InputTokensDetails? InputTokensDetails { get; set; }

    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; set; }

    [JsonPropertyName("output_tokens_details")]
    public OutputTokensDetails? OutputTokensDetails { get; set; }

    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int? TotalTokens { get; set; }

    [JsonPropertyName("estimated_cost_usd")]
    public decimal? EstimatedCostUsd { get; set; }
}

public class InputTokensDetails
{
    [JsonPropertyName("cached_tokens")]
    public int? CachedTokens { get; set; }
}

public class OutputTokensDetails
{
    [JsonPropertyName("reasoning_tokens")]
    public int? ReasoningTokens { get; set; }
}
