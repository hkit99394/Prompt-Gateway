using Microsoft.Extensions.Logging;

namespace ControlPlane.Core;

public interface IResultIngestionOrchestrator
{
    Task<ResultIngestionOutcome> IngestResultAsync(ProviderResultEvent result, CancellationToken cancellationToken);
}

public sealed class JobOrchestratorResultIngester : IResultIngestionOrchestrator
{
    private readonly JobOrchestrator _orchestrator;

    public JobOrchestratorResultIngester(JobOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public Task<ResultIngestionOutcome> IngestResultAsync(
        ProviderResultEvent result,
        CancellationToken cancellationToken)
    {
        return _orchestrator.IngestResultAsync(result, cancellationToken);
    }
}

public interface IResultMessageProcessor
{
    Task<ResultMessageProcessResult> ProcessAsync(string body, string? messageId, CancellationToken cancellationToken);
}

public sealed class ResultMessageProcessResult
{
    private ResultMessageProcessResult(bool shouldAcknowledge)
    {
        ShouldAcknowledge = shouldAcknowledge;
    }

    public bool ShouldAcknowledge { get; }

    public static ResultMessageProcessResult Acknowledge() => new(true);

    public static ResultMessageProcessResult Retry() => new(false);
}

public sealed class ResultMessageProcessor : IResultMessageProcessor
{
    private readonly IResultIngestionOrchestrator _orchestrator;
    private readonly ILogger<ResultMessageProcessor> _logger;

    public ResultMessageProcessor(
        IResultIngestionOrchestrator orchestrator,
        ILogger<ResultMessageProcessor> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task<ResultMessageProcessResult> ProcessAsync(
        string body,
        string? messageId,
        CancellationToken cancellationToken)
    {
        if (!ProviderResultEventContractMapper.TryParseWorkerResultEvent(body, out var resultEvent, out var parseError))
        {
            _logger.LogWarning(
                "Result queue message parse failed: {Error}. Acknowledging message to avoid poison. MessageId={MessageId}",
                parseError,
                messageId);
            return ResultMessageProcessResult.Acknowledge();
        }

        if (resultEvent is null)
        {
            return ResultMessageProcessResult.Acknowledge();
        }

        using (_logger.BeginScope(new Dictionary<string, object?>
               {
                   ["job_id"] = resultEvent.JobId,
                   ["attempt_id"] = resultEvent.AttemptId
               }))
        {
            try
            {
                var outcome = await _orchestrator.IngestResultAsync(resultEvent, cancellationToken);
                _logger.LogInformation(
                    "Ingested result for job {JobId} attempt {AttemptId} with outcome {Outcome}.",
                    resultEvent.JobId,
                    resultEvent.AttemptId,
                    outcome.Status);
                return ResultMessageProcessResult.Acknowledge();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Result ingestion failed. Message will retry or go to DLQ.");
                return ResultMessageProcessResult.Retry();
            }
        }
    }
}
