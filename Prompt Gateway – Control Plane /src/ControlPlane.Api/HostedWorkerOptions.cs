namespace ControlPlane.Api;

public sealed class HostedWorkerOptions
{
    public bool EnablePostAcceptResumeWorker { get; init; } = true;

    public bool EnableOutboxWorker { get; init; } = true;

    public bool EnableResultQueueWorker { get; init; } = true;
}
