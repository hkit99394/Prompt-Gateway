namespace ControlPlane.Core;

public sealed class RoutingPolicyOptions
{
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string PolicyVersion { get; init; } = "static";
    public IReadOnlyList<string> FallbackProviders { get; init; } = Array.Empty<string>();
}

public sealed class RetryPlannerOptions
{
    public int MaxAttempts { get; init; } = 3;
}
