namespace ControlPlane.Core;

public sealed class CanonicalJobRequest
{
    public string? JobId { get; init; }
    public string? AttemptId { get; init; }
    public string? TraceId { get; init; }
    public string TaskType { get; init; } = string.Empty;
    public string? InputRef { get; init; }
    public string? PromptKey { get; init; }
    public string? PromptBucket { get; init; }
    public string? PromptS3Key { get; init; }
    public string? PromptS3Bucket { get; init; }
    public string? SystemPrompt { get; init; }
    public string? Model { get; init; }
    public string? PromptInput { get; init; }
    public Dictionary<string, string>? PromptVariables { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }

    public bool HasPromptReference()
    {
        return !string.IsNullOrWhiteSpace(PromptKey)
               || !string.IsNullOrWhiteSpace(PromptS3Key)
               || !string.IsNullOrWhiteSpace(InputRef);
    }

    public CanonicalJobRequest WithIds(string jobId, string attemptId, string traceId)
    {
        return new CanonicalJobRequest
        {
            JobId = jobId,
            AttemptId = attemptId,
            TraceId = traceId,
            TaskType = TaskType,
            InputRef = InputRef,
            PromptKey = PromptKey,
            PromptBucket = PromptBucket,
            PromptS3Key = PromptS3Key,
            PromptS3Bucket = PromptS3Bucket,
            SystemPrompt = SystemPrompt,
            Model = Model,
            PromptInput = PromptInput,
            PromptVariables = PromptVariables is null ? null : new Dictionary<string, string>(PromptVariables),
            Metadata = Metadata is null ? null : new Dictionary<string, string>(Metadata)
        };
    }
}

public sealed record JobHandle(string JobId, string AttemptId, string TraceId);
