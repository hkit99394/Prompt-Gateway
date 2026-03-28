namespace ControlPlane.Api;

public sealed class ResultQueueWorkerOptions
{
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ErrorDelay { get; init; } = TimeSpan.FromSeconds(2);
}
