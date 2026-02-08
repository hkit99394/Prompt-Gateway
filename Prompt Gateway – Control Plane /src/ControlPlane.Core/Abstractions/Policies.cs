namespace ControlPlane.Core;

public interface IRoutingPolicy
{
    Task<RoutingDecision> DecideAsync(CanonicalJobRequest request, CancellationToken cancellationToken);
}

public interface IDispatchQueue
{
    Task PublishAsync(DispatchMessage message, CancellationToken cancellationToken);
}

public interface ICanonicalResponseAssembler
{
    Task<CanonicalResponse> AssembleAsync(ProviderResultEvent result, CancellationToken cancellationToken);
}

public interface IRetryPlanner
{
    RetryPlan PlanRetry(JobRecord job, JobAttempt attempt, ProviderResultEvent result);
}
