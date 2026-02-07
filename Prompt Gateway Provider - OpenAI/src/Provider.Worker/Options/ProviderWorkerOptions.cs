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

    public bool TryValidate(out string error)
    {
        if (MaxConcurrency <= 0)
        {
            error = "MaxConcurrency must be greater than 0.";
            return false;
        }

        if (MaxMessages <= 0)
        {
            error = "MaxMessages must be greater than 0.";
            return false;
        }

        if (WaitTimeSeconds < 0)
        {
            error = "WaitTimeSeconds cannot be negative.";
            return false;
        }

        if (VisibilityTimeoutSeconds < 0)
        {
            error = "VisibilityTimeoutSeconds cannot be negative.";
            return false;
        }

        if (LargePayloadThresholdBytes <= 0)
        {
            error = "LargePayloadThresholdBytes must be greater than 0.";
            return false;
        }

        if (OpenAi is null)
        {
            error = "OpenAi configuration is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(OpenAi.ApiKey))
        {
            error = "OpenAi.ApiKey is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(OpenAi.Model))
        {
            error = "OpenAi.Model is required.";
            return false;
        }

        if (OpenAi.TimeoutSeconds <= 0)
        {
            error = "OpenAi.TimeoutSeconds must be greater than 0.";
            return false;
        }

        if (OpenAi.Temperature < 0 || OpenAi.Temperature > 2)
        {
            error = "OpenAi.Temperature must be between 0 and 2.";
            return false;
        }

        if (OpenAi.MaxTokens.HasValue && OpenAi.MaxTokens.Value <= 0)
        {
            error = "OpenAi.MaxTokens must be greater than 0 when specified.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public class OpenAiOptions
{
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    [Required]
    public string Model { get; set; } = "gpt-4o-mini";

    public int TimeoutSeconds { get; set; } = 90;

    [Range(0, 2)]
    public double Temperature { get; set; } = 0.7;

    public int? MaxTokens { get; set; } = null;
}
