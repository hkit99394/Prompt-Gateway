namespace ControlPlane.Core;

public sealed class RetryPlan
{
    private RetryPlan(bool shouldRetry, string? provider, string? model, string? reason)
    {
        ShouldRetry = shouldRetry;
        Provider = provider;
        Model = model;
        Reason = reason;
    }

    public bool ShouldRetry { get; }
    public string? Provider { get; }
    public string? Model { get; }
    public string? Reason { get; }

    public static RetryPlan None(string? reason = null) => new(false, null, null, reason);

    public static RetryPlan ForProvider(string provider, string model, string? reason = null)
        => new(true, provider, model, reason);
}
