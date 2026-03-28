using System.Security.Cryptography;
using System.Text;
using ControlPlane.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Controllers;

[ApiController]
[Authorize]
[Route("jobs")]
public sealed class JobsController : ControllerBase
{
    private const string IdempotencyKeyHeaderName = "X-Idempotency-Key";
    private readonly JobOrchestrator _orchestrator;
    private readonly IPostAcceptResumeScheduler _postAcceptResumeScheduler;
    private readonly ILogger<JobsController> _logger;

    public JobsController(
        JobOrchestrator orchestrator,
        IPostAcceptResumeScheduler postAcceptResumeScheduler,
        ILogger<JobsController> logger)
    {
        _orchestrator = orchestrator;
        _postAcceptResumeScheduler = postAcceptResumeScheduler;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateJob([FromBody] CanonicalJobRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "request body is required" });
        }

        if (string.IsNullOrWhiteSpace(request.TaskType))
        {
            return BadRequest(new { error = "taskType is required" });
        }

        if (string.Equals(request.TaskType, "chat_completion", StringComparison.OrdinalIgnoreCase)
            && !request.HasPromptSource())
        {
            return BadRequest(new { error = "prompt content is required (promptText, inputRef, promptKey, or promptS3Key)" });
        }

        var normalizedRequest = NormalizeIntakeRequest(request);
        if (!string.IsNullOrWhiteSpace(normalizedRequest.JobId))
        {
            var existing = await _orchestrator.GetJobAsync(normalizedRequest.JobId, cancellationToken);
            if (existing is not null)
            {
                if (!existing.Request.IsEquivalentIntake(normalizedRequest))
                {
                    return Conflict(new
                    {
                        error = "jobId or idempotency key is already in use for a different request"
                    });
                }

                return await CreateReplayAcceptedResultAsync(existing, cancellationToken);
            }
        }

        try
        {
            var handle = await _orchestrator.AcceptAsync(normalizedRequest, cancellationToken);
            var acceptedJob = await _orchestrator.GetJobAsync(handle.JobId, cancellationToken);
            var scheduled = _postAcceptResumeScheduler.TrySchedule(handle.JobId);

            if (!scheduled)
            {
                _logger.LogWarning(
                    "Job {JobId} was accepted but automatic continuation is unavailable. Returning resumable accepted response.",
                    handle.JobId);
            }

            return CreateAcceptedResult(CreateAcceptedResponse(
                acceptedJob,
                replayed: false,
                requiresResume: !scheduled,
                warning: scheduled
                    ? null
                    : "job accepted but automatic continuation is unavailable; resume the job to continue processing",
                fallbackHandle: handle));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("{jobId}/resume")]
    public async Task<IActionResult> ResumeJob(string jobId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return BadRequest(new { error = "jobId is required" });
        }

        var existing = await _orchestrator.GetJobAsync(jobId, cancellationToken);
        if (existing is null)
        {
            return NotFound(new { error = $"job '{jobId}' was not found" });
        }

        try
        {
            var dispatch = await _orchestrator.ResumeAsync(jobId, cancellationToken);
            return Ok(new
            {
                dispatch.JobId,
                dispatch.AttemptId,
                dispatch.TraceId,
                dispatch.Provider,
                dispatch.Model,
                dispatch.IdempotencyKey
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ListJobs([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var count = Math.Clamp(limit.GetValueOrDefault(50), 1, 200);

        var jobs = await _orchestrator.ListJobsAsync(count, cancellationToken);
        return Ok(jobs);
    }

    [HttpGet("{jobId}")]
    public async Task<IActionResult> GetJob(string jobId, CancellationToken cancellationToken)
    {
        var job = await _orchestrator.GetJobAsync(jobId, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpGet("{jobId}/result")]
    public async Task<IActionResult> GetResult(string jobId, CancellationToken cancellationToken)
    {
        var result = await _orchestrator.GetFinalResultAsync(jobId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{jobId}/events")]
    public async Task<IActionResult> GetEvents(string jobId, CancellationToken cancellationToken)
    {
        var events = await _orchestrator.GetEventsAsync(jobId, cancellationToken);
        return Ok(events);
    }

    [HttpGet("{jobId}/detail")]
    public async Task<IActionResult> GetDetail(string jobId, CancellationToken cancellationToken)
    {
        var job = await _orchestrator.GetJobAsync(jobId, cancellationToken);
        if (job is null)
        {
            return NotFound();
        }

        var result = await _orchestrator.GetFinalResultAsync(jobId, cancellationToken);
        var events = await _orchestrator.GetEventsAsync(jobId, cancellationToken);

        return Ok(new
        {
            job,
            result,
            events
        });
    }

    private CanonicalJobRequest NormalizeIntakeRequest(CanonicalJobRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.JobId))
        {
            return request;
        }

        if (!Request.Headers.TryGetValue(IdempotencyKeyHeaderName, out var values))
        {
            return request;
        }

        var idempotencyKey = values.ToString().Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return request;
        }

        return request.WithJobId(CreateDeterministicJobId(idempotencyKey));
    }

    private async Task<IActionResult> CreateReplayAcceptedResultAsync(
        JobRecord existing,
        CancellationToken cancellationToken)
    {
        if (existing.State is not (JobState.Created or JobState.Routed or JobState.Retrying))
        {
            return CreateAcceptedResult(CreateAcceptedResponse(
                existing,
                replayed: true,
                requiresResume: false,
                warning: null));
        }

        var scheduled = _postAcceptResumeScheduler.TrySchedule(existing.JobId);
        if (!scheduled)
        {
            _logger.LogWarning(
                "Existing accepted job {JobId} could not be scheduled for automatic continuation during idempotent replay.",
                existing.JobId);
        }

        var refreshed = await _orchestrator.GetJobAsync(existing.JobId, cancellationToken) ?? existing;
        return CreateAcceptedResult(CreateAcceptedResponse(
            refreshed,
            replayed: true,
            requiresResume: !scheduled,
            warning: scheduled
                ? null
                : "existing accepted job still requires resume to continue processing"));
    }

    private static string CreateDeterministicJobId(string idempotencyKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey));
        return $"job-intake-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private AcceptedAtActionResult CreateAcceptedResult(AcceptedJobResponse response)
    {
        return AcceptedAtAction(nameof(GetJob), new { jobId = response.JobId }, response);
    }

    private static AcceptedJobResponse CreateAcceptedResponse(
        JobRecord? job,
        bool replayed,
        bool requiresResume,
        string? warning,
        string? idempotencyKeyOverride = null,
        JobHandle? fallbackHandle = null)
    {
        if (job is null && fallbackHandle is null)
        {
            throw new ArgumentException("Either a persisted job or fallback handle is required.");
        }

        var jobId = job?.JobId ?? fallbackHandle!.JobId;
        var attemptId = job?.CurrentAttemptId ?? fallbackHandle!.AttemptId;
        var traceId = job?.TraceId ?? fallbackHandle!.TraceId;
        var state = job?.State.ToString() ?? JobState.Created.ToString();
        var idempotencyKey = idempotencyKeyOverride;
        if (idempotencyKey is null && job is not null && job.State is not JobState.Created and not JobState.Routed)
        {
            idempotencyKey = $"{job.JobId}:{job.CurrentAttemptId}";
        }

        return new AcceptedJobResponse(
            jobId,
            attemptId,
            traceId,
            state,
            true,
            replayed,
            requiresResume,
            idempotencyKey,
            warning);
    }
}

public sealed record AcceptedJobResponse(
    string JobId,
    string AttemptId,
    string TraceId,
    string State,
    bool Accepted,
    bool Replayed,
    bool RequiresResume,
    string? IdempotencyKey,
    string? Warning);
