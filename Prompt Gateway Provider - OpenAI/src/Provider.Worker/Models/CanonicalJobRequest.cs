using System.Text.Json.Serialization;

namespace Provider.Worker.Models;

public static class CanonicalTaskTypes
{
    public const string ChatCompletion = "chat_completion";
}

public class CanonicalJobRequest
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("attempt_id")]
    public string AttemptId { get; set; } = string.Empty;

    [JsonPropertyName("task_type")]
    public string TaskType { get; set; } = string.Empty;

    [JsonPropertyName("prompt_s3_key")]
    public string PromptS3Key { get; set; } = string.Empty;

    [JsonPropertyName("prompt_s3_bucket")]
    public string? PromptS3Bucket { get; set; }

    [JsonPropertyName("system_prompt")]
    public string? SystemPrompt { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("parameters")]
    public OpenAiParameters? Parameters { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}
