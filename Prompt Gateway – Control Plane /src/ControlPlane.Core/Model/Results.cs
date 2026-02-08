namespace ControlPlane.Core;

public sealed class CanonicalError
{
    public CanonicalError(string code, string message)
    {
        Code = code;
        Message = message;
    }

    public string Code { get; init; }
    public string Message { get; init; }
    public string? ProviderCode { get; init; }
}

public sealed class ProviderResultEvent
{
    public string JobId { get; init; } = string.Empty;
    public string AttemptId { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string? OutputRef { get; init; }
    public UsageMetrics? Usage { get; init; }
    public CostMetrics? Cost { get; init; }
    public CanonicalError? Error { get; init; }
}

public sealed class CanonicalResponse
{
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string? OutputRef { get; init; }
    public UsageMetrics? Usage { get; init; }
    public CostMetrics? Cost { get; init; }
    public CanonicalError? Error { get; init; }
}
