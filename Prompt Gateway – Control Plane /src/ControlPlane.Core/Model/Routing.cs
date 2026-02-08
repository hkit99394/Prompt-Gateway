namespace ControlPlane.Core;

public sealed class RoutingDecision
{
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string PolicyVersion { get; init; } = string.Empty;
    public IReadOnlyList<string> FallbackProviders { get; init; } = Array.Empty<string>();
    public Dictionary<string, string>? Inputs { get; init; }
}

public sealed class DispatchMessage
{
    public string JobId { get; init; } = string.Empty;
    public string AttemptId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
    public CanonicalJobRequest Request { get; init; } = new();
}
