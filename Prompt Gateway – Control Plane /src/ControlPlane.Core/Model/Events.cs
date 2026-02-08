namespace ControlPlane.Core;

public sealed class JobEvent
{
    public JobEvent(string jobId, string attemptId, JobEventType type, DateTimeOffset occurredAt)
    {
        JobId = jobId;
        AttemptId = attemptId;
        Type = type;
        OccurredAt = occurredAt;
    }

    public string JobId { get; init; }
    public string AttemptId { get; init; }
    public JobEventType Type { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public Dictionary<string, string>? Attributes { get; init; }

    public static JobEvent Created(string jobId, string attemptId, DateTimeOffset occurredAt)
    {
        return new JobEvent(jobId, attemptId, JobEventType.Created, occurredAt);
    }

    public static JobEvent Routed(string jobId, string attemptId, DateTimeOffset occurredAt, RoutingDecision decision)
    {
        return new JobEvent(jobId, attemptId, JobEventType.Routed, occurredAt)
        {
            Attributes = new Dictionary<string, string>
            {
                ["provider"] = decision.Provider,
                ["model"] = decision.Model,
                ["policy_version"] = decision.PolicyVersion
            }
        };
    }

    public static JobEvent Dispatched(string jobId, string attemptId, DateTimeOffset occurredAt, DispatchMessage dispatch)
    {
        return new JobEvent(jobId, attemptId, JobEventType.Dispatched, occurredAt)
        {
            Attributes = new Dictionary<string, string>
            {
                ["provider"] = dispatch.Provider,
                ["model"] = dispatch.Model,
                ["idempotency_key"] = dispatch.IdempotencyKey
            }
        };
    }

    public static JobEvent Completed(string jobId, string attemptId, DateTimeOffset occurredAt, CanonicalResponse response)
    {
        var attributes = new Dictionary<string, string>
        {
            ["provider"] = response.Provider,
            ["model"] = response.Model
        };

        if (!string.IsNullOrWhiteSpace(response.OutputRef))
        {
            attributes["output_ref"] = response.OutputRef!;
        }

        return new JobEvent(jobId, attemptId, JobEventType.Completed, occurredAt)
        {
            Attributes = attributes
        };
    }

    public static JobEvent Failed(string jobId, string attemptId, DateTimeOffset occurredAt, CanonicalError error)
    {
        return new JobEvent(jobId, attemptId, JobEventType.Failed, occurredAt)
        {
            Attributes = new Dictionary<string, string>
            {
                ["error_code"] = error.Code,
                ["error_message"] = error.Message
            }
        };
    }

    public static JobEvent Retried(string jobId, string attemptId, DateTimeOffset occurredAt, RetryPlan plan)
    {
        var attributes = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(plan.Provider))
        {
            attributes["provider"] = plan.Provider!;
        }

        if (!string.IsNullOrWhiteSpace(plan.Model))
        {
            attributes["model"] = plan.Model!;
        }

        return new JobEvent(jobId, attemptId, JobEventType.Retried, occurredAt)
        {
            Attributes = attributes
        };
    }
}
