using System.ComponentModel.DataAnnotations;

namespace Provider.Worker.Options;

public class ProviderWorkerOptions
{
    public const string SectionName = "ProviderWorker";

    public string ProviderName { get; set; } = "openai";

    [Required]
    public string InputQueueUrl { get; set; } = string.Empty;

    [Required]
    public string OutputQueueUrl { get; set; } = string.Empty;

    public string PromptBucket { get; set; } = string.Empty;

    public string ResultBucket { get; set; } = string.Empty;

    public string ResultPrefix { get; set; } = "results/";

    public int MaxConcurrency { get; set; } = 4;

    public int MaxMessages { get; set; } = 5;

    public int WaitTimeSeconds { get; set; } = 20;

    public int VisibilityTimeoutSeconds { get; set; } = 120;

    public int LargePayloadThresholdBytes { get; set; } = 64 * 1024;

    public int DedupeMemoryTtlMinutes { get; set; } = 60;

    public string DedupeTableName { get; set; } = string.Empty;

    public OpenAiOptions OpenAi { get; set; } = new();
}

public class OpenAiOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    public string Model { get; set; } = "gpt-4o-mini";

    public int TimeoutSeconds { get; set; } = 90;

    public double Temperature { get; set; } = 0.7;

    public int? MaxTokens { get; set; } = null;
}
