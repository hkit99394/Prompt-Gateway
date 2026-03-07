using ControlPlane.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Controllers;

[ApiController]
[Authorize]
[Route("jobs")]
public sealed class JobsController : ControllerBase
{
    private readonly JobOrchestrator _orchestrator;

    public JobsController(JobOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
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

        try
        {
            var handle = await _orchestrator.AcceptAsync(request, cancellationToken);
            var routing = await _orchestrator.RouteAsync(handle.JobId, cancellationToken);
            var dispatch = await _orchestrator.DispatchAsync(handle.JobId, handle.AttemptId, cancellationToken);

            return Ok(new
            {
                handle.JobId,
                handle.AttemptId,
                handle.TraceId,
                routing,
                dispatch.IdempotencyKey
            });
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
}
