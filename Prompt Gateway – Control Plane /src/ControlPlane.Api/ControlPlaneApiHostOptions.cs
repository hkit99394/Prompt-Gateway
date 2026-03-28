namespace ControlPlane.Api;

public sealed class ControlPlaneApiHostOptions
{
    public bool EnableSwagger { get; set; } = true;

    public HostedWorkerOptions HostedWorkers { get; set; } = new();

    public OutboxWorkerOptions OutboxWorker { get; set; } = new();

    public ResultQueueWorkerOptions ResultQueueWorker { get; set; } = new();
}
