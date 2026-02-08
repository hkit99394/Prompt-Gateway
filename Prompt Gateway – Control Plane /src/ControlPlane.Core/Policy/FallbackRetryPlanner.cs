namespace ControlPlane.Core;

public sealed class FallbackRetryPlanner : IRetryPlanner
{
    private readonly RetryPlannerOptions _options;

    public FallbackRetryPlanner(RetryPlannerOptions options)
    {
        _options = options;
    }

    public RetryPlan PlanRetry(JobRecord job, JobAttempt attempt, ProviderResultEvent result)
    {
        if (result.IsSuccess)
        {
            return RetryPlan.None("success");
        }

        if (job.Attempts.Count >= _options.MaxAttempts)
        {
            return RetryPlan.None("max_attempts");
        }

        var fallbackProviders = attempt.RoutingDecision?.FallbackProviders;
        if (fallbackProviders is null || fallbackProviders.Count == 0)
        {
            return RetryPlan.None("no_fallbacks");
        }

        var usedProviders = new HashSet<string>(
            job.Attempts
                .Select(existing => existing.Provider)
                .Where(provider => !string.IsNullOrWhiteSpace(provider))
                .Select(provider => provider!),
            StringComparer.OrdinalIgnoreCase);

        var nextProvider = fallbackProviders.FirstOrDefault(provider => !usedProviders.Contains(provider));
        if (string.IsNullOrWhiteSpace(nextProvider))
        {
            return RetryPlan.None("fallbacks_exhausted");
        }

        var model = string.IsNullOrWhiteSpace(attempt.Model) ? string.Empty : attempt.Model;
        return RetryPlan.ForProvider(nextProvider, model, "fallback");
    }
}
