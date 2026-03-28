namespace ControlPlane.Core;

public sealed class CanonicalJobRequest
{
    public string? JobId { get; init; }
    public string? AttemptId { get; init; }
    public string? TraceId { get; init; }
    public string TaskType { get; init; } = string.Empty;
    public string? PromptText { get; init; }
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

    public bool HasPromptSource()
    {
        return !string.IsNullOrWhiteSpace(PromptText) || HasPromptReference();
    }

    public CanonicalJobRequest WithIds(string jobId, string attemptId, string traceId)
    {
        return new CanonicalJobRequest
        {
            JobId = jobId,
            AttemptId = attemptId,
            TraceId = traceId,
            TaskType = TaskType,
            PromptText = PromptText,
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

    public CanonicalJobRequest WithJobId(string jobId)
    {
        return new CanonicalJobRequest
        {
            JobId = jobId,
            AttemptId = AttemptId,
            TraceId = TraceId,
            TaskType = TaskType,
            PromptText = PromptText,
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

    public bool IsEquivalentIntake(CanonicalJobRequest? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(TaskType, other.TaskType, StringComparison.Ordinal)
               && string.Equals(PromptText, other.PromptText, StringComparison.Ordinal)
               && string.Equals(InputRef, other.InputRef, StringComparison.Ordinal)
               && string.Equals(PromptKey, other.PromptKey, StringComparison.Ordinal)
               && string.Equals(PromptBucket, other.PromptBucket, StringComparison.Ordinal)
               && string.Equals(PromptS3Key, other.PromptS3Key, StringComparison.Ordinal)
               && string.Equals(PromptS3Bucket, other.PromptS3Bucket, StringComparison.Ordinal)
               && string.Equals(SystemPrompt, other.SystemPrompt, StringComparison.Ordinal)
               && string.Equals(Model, other.Model, StringComparison.Ordinal)
               && string.Equals(PromptInput, other.PromptInput, StringComparison.Ordinal)
               && DictionaryEquals(PromptVariables, other.PromptVariables)
               && DictionaryEquals(Metadata, other.Metadata);
    }

    private static bool DictionaryEquals(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value)
                || !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record JobHandle(string JobId, string AttemptId, string TraceId);
