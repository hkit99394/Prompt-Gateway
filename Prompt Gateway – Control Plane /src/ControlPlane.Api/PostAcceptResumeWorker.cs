using ControlPlane.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api;

public sealed class PostAcceptResumeWorker : BackgroundService
{
    private readonly PostAcceptResumeQueue _queue;
    private readonly JobOrchestrator _orchestrator;
    private readonly ILogger<PostAcceptResumeWorker> _logger;

    public PostAcceptResumeWorker(
        PostAcceptResumeQueue queue,
        JobOrchestrator orchestrator,
        ILogger<PostAcceptResumeWorker> logger)
    {
        _queue = queue;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _orchestrator.ResumeAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Accepted job {JobId} could not be continued automatically.", jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Accepted job {JobId} failed automatic continuation.", jobId);
            }
        }
    }
}
