namespace ControlPlane.Core;

public sealed class JobAttempt
{
    public JobAttempt(string attemptId, DateTimeOffset createdAt)
    {
        AttemptId = attemptId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public string AttemptId { get; }
    public AttemptState State { get; private set; } = AttemptState.Created;
    public string? Provider { get; private set; }
    public string? Model { get; private set; }
    public RoutingDecision? RoutingDecision { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void ApplyRouting(RoutingDecision decision, DateTimeOffset updatedAt)
    {
        RoutingDecision = decision;
        Provider = decision.Provider;
        Model = decision.Model;
        State = AttemptState.Routed;
        UpdatedAt = updatedAt;
    }

    public void SetState(AttemptState state, DateTimeOffset updatedAt)
    {
        State = state;
        UpdatedAt = updatedAt;
    }
}

public sealed class JobRecord
{
    private readonly List<JobAttempt> _attempts = new();

    private JobRecord(
        string jobId,
        string traceId,
        CanonicalJobRequest request,
        DateTimeOffset createdAt,
        string attemptId)
    {
        JobId = jobId;
        TraceId = traceId;
        Request = request;
        State = JobState.Created;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        CurrentAttemptId = attemptId;
    }

    public string JobId { get; }
    public string TraceId { get; }
    public JobState State { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string CurrentAttemptId { get; private set; }
    public CanonicalJobRequest Request { get; }
    public IReadOnlyList<JobAttempt> Attempts => _attempts;

    public static JobRecord Create(CanonicalJobRequest request, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(request.JobId))
        {
            throw new ArgumentException("JobId is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.AttemptId))
        {
            throw new ArgumentException("AttemptId is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.TraceId))
        {
            throw new ArgumentException("TraceId is required.", nameof(request));
        }

        var record = new JobRecord(request.JobId!, request.TraceId!, request, createdAt, request.AttemptId!);
        record._attempts.Add(new JobAttempt(request.AttemptId!, createdAt));
        return record;
    }

    public static JobRecord Restore(JobRecordSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.JobId))
        {
            throw new ArgumentException("JobId is required.", nameof(snapshot));
        }

        if (string.IsNullOrWhiteSpace(snapshot.TraceId))
        {
            throw new ArgumentException("TraceId is required.", nameof(snapshot));
        }

        if (snapshot.Request is null)
        {
            throw new ArgumentException("Request is required.", nameof(snapshot));
        }

        if (string.IsNullOrWhiteSpace(snapshot.CurrentAttemptId))
        {
            throw new ArgumentException("CurrentAttemptId is required.", nameof(snapshot));
        }

        var record = new JobRecord(
            snapshot.JobId,
            snapshot.TraceId,
            snapshot.Request,
            snapshot.CreatedAt,
            snapshot.CurrentAttemptId);

        record._attempts.Clear();
        var attempts = snapshot.Attempts ?? Array.Empty<JobAttemptSnapshot>();
        foreach (var attemptSnapshot in attempts)
        {
            var attempt = new JobAttempt(attemptSnapshot.AttemptId, attemptSnapshot.CreatedAt);
            if (attemptSnapshot.RoutingDecision is not null)
            {
                attempt.ApplyRouting(attemptSnapshot.RoutingDecision, attemptSnapshot.UpdatedAt);
            }

            attempt.SetState(attemptSnapshot.State, attemptSnapshot.UpdatedAt);
            record._attempts.Add(attempt);
        }

        record.CurrentAttemptId = snapshot.CurrentAttemptId;
        record.SetState(snapshot.State, snapshot.UpdatedAt);
        return record;
    }

    public JobAttempt? GetAttempt(string attemptId)
    {
        return _attempts.FirstOrDefault(attempt => attempt.AttemptId == attemptId);
    }

    public JobAttempt AddAttempt(string attemptId, DateTimeOffset updatedAt)
    {
        var attempt = new JobAttempt(attemptId, updatedAt);
        _attempts.Add(attempt);
        CurrentAttemptId = attemptId;
        UpdatedAt = updatedAt;
        return attempt;
    }

    public void SetState(JobState state, DateTimeOffset updatedAt)
    {
        State = state;
        UpdatedAt = updatedAt;
    }
}
