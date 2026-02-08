namespace ControlPlane.Core;

public sealed class CanonicalJobRequest
{
    public string? JobId { get; init; }
    public string? AttemptId { get; init; }
    public string? TraceId { get; init; }
    public string TaskType { get; init; } = string.Empty;
    public string? InputRef { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }

    public CanonicalJobRequest WithIds(string jobId, string attemptId, string traceId)
    {
        return new CanonicalJobRequest
        {
            JobId = jobId,
            AttemptId = attemptId,
            TraceId = traceId,
            TaskType = TaskType,
            InputRef = InputRef,
            Metadata = Metadata is null ? null : new Dictionary<string, string>(Metadata)
        };
    }
}

public sealed record JobHandle(string JobId, string AttemptId, string TraceId);
