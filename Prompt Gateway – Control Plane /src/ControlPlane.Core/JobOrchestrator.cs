using Microsoft.Extensions.Logging;

namespace ControlPlane.Core;

public sealed class JobOrchestrator
{
    private readonly ILogger<JobOrchestrator> _logger;
    private readonly IJobStore _jobStore;
    private readonly IJobEventStore _eventStore;
    private readonly IRoutingPolicy _routingPolicy;
    private readonly IOutboxStore _outboxStore;
    private readonly IDeduplicationStore _dedupeStore;
    private readonly ICanonicalResponseAssembler _responseAssembler;
    private readonly IResultStore _resultStore;
    private readonly IRetryPlanner _retryPlanner;
    private readonly IIdGenerator _idGenerator;
    private readonly IClock _clock;

    public JobOrchestrator(
        ILogger<JobOrchestrator> logger,
        IJobStore jobStore,
        IJobEventStore eventStore,
        IRoutingPolicy routingPolicy,
        IOutboxStore outboxStore,
        IDeduplicationStore dedupeStore,
        ICanonicalResponseAssembler responseAssembler,
        IResultStore resultStore,
        IRetryPlanner retryPlanner,
        IIdGenerator idGenerator,
        IClock clock)
    {
        _logger = logger;
        _jobStore = jobStore;
        _eventStore = eventStore;
        _routingPolicy = routingPolicy;
        _outboxStore = outboxStore;
        _dedupeStore = dedupeStore;
        _responseAssembler = responseAssembler;
        _resultStore = resultStore;
        _retryPlanner = retryPlanner;
        _idGenerator = idGenerator;
        _clock = clock;
    }

    public async Task<JobHandle> AcceptAsync(CanonicalJobRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.TaskType))
        {
            throw new ArgumentException("TaskType is required.", nameof(request));
        }

        var jobId = string.IsNullOrWhiteSpace(request.JobId) ? _idGenerator.NewId("job") : request.JobId!;
        var attemptId = string.IsNullOrWhiteSpace(request.AttemptId) ? _idGenerator.NewId("attempt") : request.AttemptId!;
        var traceId = string.IsNullOrWhiteSpace(request.TraceId) ? _idGenerator.NewTraceId() : request.TraceId!;
        var now = _clock.UtcNow;

        var normalized = request.WithIds(jobId, attemptId, traceId);
        var job = JobRecord.Create(normalized, now);

        await _jobStore.CreateAsync(job, cancellationToken);
        await _eventStore.AppendAsync(JobEvent.Created(jobId, attemptId, now), cancellationToken);

        using (_logger.BeginScope(new Dictionary<string, object?>
               {
                   ["job_id"] = jobId,
                   ["attempt_id"] = attemptId,
                   ["trace_id"] = traceId
               }))
        {
            _logger.LogInformation("Accepted job request for task {TaskType}.", request.TaskType);
        }

        return new JobHandle(jobId, attemptId, traceId);
    }

    public async Task<RoutingDecision> RouteAsync(string jobId, CancellationToken cancellationToken)
    {
        var job = await _jobStore.GetAsync(jobId, cancellationToken);
        if (job is null)
        {
            throw new InvalidOperationException($"Job '{jobId}' was not found.");
        }

        var attempt = job.GetAttempt(job.CurrentAttemptId);
        if (attempt is null)
        {
            throw new InvalidOperationException($"Attempt '{job.CurrentAttemptId}' was not found.");
        }

        var decision = await _routingPolicy.DecideAsync(job.Request, cancellationToken);
        var now = _clock.UtcNow;
        attempt.ApplyRouting(decision, now);
        job.SetState(JobState.Routed, now);

        await _jobStore.UpdateAsync(job, cancellationToken);
        await _eventStore.AppendAsync(JobEvent.Routed(jobId, attempt.AttemptId, now, decision), cancellationToken);

        using (_logger.BeginScope(new Dictionary<string, object?>
               {
                   ["job_id"] = jobId,
                   ["attempt_id"] = attempt.AttemptId,
                   ["provider"] = decision.Provider,
                   ["model"] = decision.Model
               }))
        {
            _logger.LogInformation("Routing decision selected provider {Provider}.", decision.Provider);
        }

        return decision;
    }

    public async Task<DispatchMessage> DispatchAsync(
        string jobId,
        string attemptId,
        CancellationToken cancellationToken)
    {
        var job = await _jobStore.GetAsync(jobId, cancellationToken);
        if (job is null)
        {
            throw new InvalidOperationException($"Job '{jobId}' was not found.");
        }

        var attempt = job.GetAttempt(attemptId);
        if (attempt is null)
        {
            throw new InvalidOperationException($"Attempt '{attemptId}' was not found.");
        }

        if (attempt.RoutingDecision is null || string.IsNullOrWhiteSpace(attempt.Provider))
        {
            throw new InvalidOperationException($"Attempt '{attemptId}' has not been routed.");
        }

        var now = _clock.UtcNow;
        var dispatch = new DispatchMessage
        {
            JobId = jobId,
            AttemptId = attemptId,
            TraceId = job.TraceId,
            Provider = attempt.Provider!,
            Model = attempt.Model ?? string.Empty,
            IdempotencyKey = $"{jobId}:{attemptId}",
            Request = job.Request
        };

        var outbox = new OutboxDispatchMessage(_idGenerator.NewId("outbox"), dispatch, now);
        await _outboxStore.EnqueueDispatchAsync(outbox, cancellationToken);

        attempt.SetState(AttemptState.Dispatched, now);
        job.SetState(JobState.Dispatched, now);

        await _jobStore.UpdateAsync(job, cancellationToken);
        await _eventStore.AppendAsync(JobEvent.Dispatched(jobId, attemptId, now, dispatch), cancellationToken);

        using (_logger.BeginScope(new Dictionary<string, object?>
               {
                   ["job_id"] = jobId,
                   ["attempt_id"] = attemptId,
                   ["provider"] = dispatch.Provider,
                   ["model"] = dispatch.Model
               }))
        {
            _logger.LogInformation("Dispatch queued with idempotency key {IdempotencyKey}.", dispatch.IdempotencyKey);
        }

        return dispatch;
    }

    public async Task<ResultIngestionOutcome> IngestResultAsync(
        ProviderResultEvent result,
        CancellationToken cancellationToken)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (!await _dedupeStore.TryStartAsync(result.JobId, result.AttemptId, cancellationToken))
        {
            return ResultIngestionOutcome.Duplicate();
        }

        var job = await _jobStore.GetAsync(result.JobId, cancellationToken);
        if (job is null)
        {
            await _dedupeStore.MarkCompletedAsync(result.JobId, result.AttemptId, cancellationToken);
            return ResultIngestionOutcome.JobNotFound();
        }

        var attempt = job.GetAttempt(result.AttemptId);
        if (attempt is null)
        {
            await _dedupeStore.MarkCompletedAsync(result.JobId, result.AttemptId, cancellationToken);
            return ResultIngestionOutcome.JobNotFound();
        }

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["job_id"] = result.JobId,
            ["attempt_id"] = result.AttemptId,
            ["provider"] = result.Provider,
            ["model"] = result.Model
        });

        var now = _clock.UtcNow;

        if (result.IsSuccess)
        {
            var response = await _responseAssembler.AssembleAsync(result, cancellationToken);
            attempt.SetState(AttemptState.Completed, now);
            job.SetState(JobState.Completed, now);

            await _resultStore.SaveAttemptResultAsync(job.JobId, attempt.AttemptId, response, cancellationToken);
            await _resultStore.SaveFinalResultAsync(job.JobId, response, cancellationToken);
            await _jobStore.UpdateAsync(job, cancellationToken);
            await _eventStore.AppendAsync(JobEvent.Completed(job.JobId, attempt.AttemptId, now, response), cancellationToken);
            await _dedupeStore.MarkCompletedAsync(job.JobId, attempt.AttemptId, cancellationToken);

            _logger.LogInformation("Result finalized for job.");
            return ResultIngestionOutcome.Finalized(response);
        }

        var retryPlan = _retryPlanner.PlanRetry(job, attempt, result);
        if (retryPlan.ShouldRetry && !string.IsNullOrWhiteSpace(retryPlan.Provider))
        {
            var nextAttemptId = _idGenerator.NewId("attempt");
            var newAttempt = job.AddAttempt(nextAttemptId, now);
            newAttempt.ApplyRouting(new RoutingDecision
            {
                Provider = retryPlan.Provider!,
                Model = retryPlan.Model ?? string.Empty,
                PolicyVersion = "retry",
                FallbackProviders = Array.Empty<string>()
            }, now);

            job.SetState(JobState.Retrying, now);
            await _eventStore.AppendAsync(JobEvent.Retried(job.JobId, attempt.AttemptId, now, retryPlan), cancellationToken);

            var dispatch = new DispatchMessage
            {
                JobId = job.JobId,
                AttemptId = newAttempt.AttemptId,
                TraceId = job.TraceId,
                Provider = newAttempt.Provider ?? string.Empty,
                Model = newAttempt.Model ?? string.Empty,
                IdempotencyKey = $"{job.JobId}:{newAttempt.AttemptId}",
                Request = job.Request.WithIds(job.JobId, newAttempt.AttemptId, job.TraceId)
            };

            var outbox = new OutboxDispatchMessage(_idGenerator.NewId("outbox"), dispatch, now);
            await _outboxStore.EnqueueDispatchAsync(outbox, cancellationToken);
            await _jobStore.UpdateAsync(job, cancellationToken);
            await _dedupeStore.MarkCompletedAsync(job.JobId, attempt.AttemptId, cancellationToken);

            _logger.LogWarning("Result failed; retrying with provider {Provider}.", retryPlan.Provider);
            return ResultIngestionOutcome.Retrying(dispatch);
        }

        var errorResponse = await _responseAssembler.AssembleAsync(result, cancellationToken);
        attempt.SetState(AttemptState.Failed, now);
        job.SetState(JobState.Failed, now);

        await _resultStore.SaveAttemptResultAsync(job.JobId, attempt.AttemptId, errorResponse, cancellationToken);
        await _resultStore.SaveFinalResultAsync(job.JobId, errorResponse, cancellationToken);
        await _jobStore.UpdateAsync(job, cancellationToken);

        if (errorResponse.Error is not null)
        {
            await _eventStore.AppendAsync(JobEvent.Failed(job.JobId, attempt.AttemptId, now, errorResponse.Error), cancellationToken);
        }

        await _dedupeStore.MarkCompletedAsync(job.JobId, attempt.AttemptId, cancellationToken);
        _logger.LogError("Result failed with no retry plan.");
        return ResultIngestionOutcome.Finalized(errorResponse);
    }

    public Task<JobRecord?> GetJobAsync(string jobId, CancellationToken cancellationToken)
        => _jobStore.GetAsync(jobId, cancellationToken);

    public Task<CanonicalResponse?> GetFinalResultAsync(string jobId, CancellationToken cancellationToken)
        => _resultStore.GetFinalResultAsync(jobId, cancellationToken);

    public Task<IReadOnlyList<JobEvent>> GetEventsAsync(string jobId, CancellationToken cancellationToken)
        => _eventStore.GetAsync(jobId, cancellationToken);
}
