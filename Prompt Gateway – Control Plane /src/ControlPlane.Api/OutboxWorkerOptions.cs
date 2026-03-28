namespace ControlPlane.Api;

public sealed class OutboxWorkerOptions
{
    public int MaxMessagesPerCycle { get; init; } = 25;

    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ErrorDelay { get; init; } = TimeSpan.FromSeconds(2);
}
