namespace ControlPlane.Core;

public sealed record JobSummary(
    string JobId,
    string TraceId,
    string CurrentAttemptId,
    JobState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
