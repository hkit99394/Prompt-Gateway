namespace ControlPlane.Core;

public sealed record JobAttemptSnapshot(
    string AttemptId,
    AttemptState State,
    string? Provider,
    string? Model,
    RoutingDecision? RoutingDecision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record JobRecordSnapshot(
    string JobId,
    string TraceId,
    JobState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CurrentAttemptId,
    CanonicalJobRequest Request,
    IReadOnlyList<JobAttemptSnapshot> Attempts);
