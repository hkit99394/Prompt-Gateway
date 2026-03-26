namespace ControlPlane.OutboxLambda;

public sealed class OutboxLambdaOptions
{
    public int MaxMessagesPerInvocation { get; init; } = 25;
}

public sealed class OutboxDispatchInvocationResult
{
    public int ProcessedCount { get; init; }

    public bool ReachedLimit { get; init; }
}
