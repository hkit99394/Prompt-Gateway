namespace ControlPlane.Core;

public sealed class StaticRoutingPolicy : IRoutingPolicy
{
    private readonly RoutingPolicyOptions _options;

    public StaticRoutingPolicy(RoutingPolicyOptions options)
    {
        _options = options;
    }

    public Task<RoutingDecision> DecideAsync(CanonicalJobRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Provider))
        {
            throw new InvalidOperationException("RoutingPolicyOptions.Provider is required.");
        }

        var decision = new RoutingDecision
        {
            Provider = _options.Provider,
            Model = _options.Model,
            PolicyVersion = _options.PolicyVersion,
            FallbackProviders = _options.FallbackProviders
        };

        return Task.FromResult(decision);
    }
}
