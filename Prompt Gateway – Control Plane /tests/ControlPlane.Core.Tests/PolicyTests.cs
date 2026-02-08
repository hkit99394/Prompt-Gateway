namespace ControlPlane.Core.Tests;

public class PolicyTests
{
    [Test]
    public async Task StaticRoutingPolicy_UsesConfiguredValues()
    {
        var policy = new StaticRoutingPolicy(new RoutingPolicyOptions
        {
            Provider = "openai",
            Model = "gpt-4.1",
            PolicyVersion = "static-v1",
            FallbackProviders = new[] { "anthropic" }
        });

        var decision = await policy.DecideAsync(new CanonicalJobRequest { TaskType = "chat_completion" }, CancellationToken.None);

        Assert.That(decision.Provider, Is.EqualTo("openai"));
        Assert.That(decision.Model, Is.EqualTo("gpt-4.1"));
        Assert.That(decision.PolicyVersion, Is.EqualTo("static-v1"));
        Assert.That(decision.FallbackProviders, Is.EquivalentTo(new[] { "anthropic" }));
    }

    [Test]
    public void FallbackRetryPlanner_SelectsNextFallbackProvider()
    {
        var options = new RetryPlannerOptions { MaxAttempts = 3 };
        var planner = new FallbackRetryPlanner(options);

        var request = new CanonicalJobRequest
        {
            JobId = "job-8",
            AttemptId = "attempt-8",
            TraceId = "trace-8",
            TaskType = "chat_completion"
        };

        var now = new DateTimeOffset(2026, 2, 8, 0, 0, 0, TimeSpan.Zero);
        var job = JobRecord.Create(request, now);
        var attempt = job.GetAttempt("attempt-8")!;
        attempt.ApplyRouting(new RoutingDecision
        {
            Provider = "openai",
            Model = "gpt-4.1",
            PolicyVersion = "v1",
            FallbackProviders = new[] { "anthropic" }
        }, now);

        var plan = planner.PlanRetry(job, attempt, new ProviderResultEvent
        {
            JobId = "job-8",
            AttemptId = "attempt-8",
            Provider = "openai",
            Model = "gpt-4.1",
            IsSuccess = false
        });

        Assert.That(plan.ShouldRetry, Is.True);
        Assert.That(plan.Provider, Is.EqualTo("anthropic"));
        Assert.That(plan.Model, Is.EqualTo("gpt-4.1"));
    }
}
