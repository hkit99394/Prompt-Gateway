using Amazon.SQS;
using Amazon.SQS.Model;
using ControlPlane.Aws;
using ControlPlane.Core;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api;

/// <summary>
/// Polls the result queue, parses worker result events, and ingests them via the orchestrator.
/// </summary>
public sealed class ResultQueueProcessor
{
    private readonly IAmazonSQS _sqs;
    private readonly AwsQueueOptions _options;
    private readonly JobOrchestrator _orchestrator;
    private readonly ILogger<ResultQueueProcessor> _logger;

    public ResultQueueProcessor(
        IAmazonSQS sqs,
        AwsQueueOptions options,
        JobOrchestrator orchestrator,
        ILogger<ResultQueueProcessor> logger)
    {
        _sqs = sqs;
        _options = options;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Receives at most one message from the result queue, ingests it, and deletes it on success.
    /// Returns true if a message was processed, false if the queue was empty or not configured.
    /// </summary>
    public async Task<bool> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ResultQueueUrl))
            return false;

        var request = new ReceiveMessageRequest
        {
            QueueUrl = _options.ResultQueueUrl,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 10,
            MessageAttributeNames = new List<string> { "All" }
        };

        var response = await _sqs.ReceiveMessageAsync(request, cancellationToken);
        if (response.Messages is null || response.Messages.Count == 0)
            return false;

        var message = response.Messages[0];
        var body = message.Body ?? string.Empty;

        if (!ProviderResultEventContractMapper.TryParseWorkerResultEvent(body, out var resultEvent, out var parseError))
        {
            _logger.LogWarning("Result queue message parse failed: {Error}. Deleting message to avoid poison. MessageId={MessageId}",
                parseError, message.MessageId);
            await _sqs.DeleteMessageAsync(_options.ResultQueueUrl, message.ReceiptHandle, cancellationToken);
            return true;
        }

        if (resultEvent is null)
        {
            await _sqs.DeleteMessageAsync(_options.ResultQueueUrl, message.ReceiptHandle, cancellationToken);
            return true;
        }

        using (_logger.BeginScope(new Dictionary<string, object?>
               {
                   ["job_id"] = resultEvent.JobId,
                   ["attempt_id"] = resultEvent.AttemptId
               }))
        {
            try
            {
                await _orchestrator.IngestResultAsync(resultEvent, cancellationToken);
                await _sqs.DeleteMessageAsync(_options.ResultQueueUrl, message.ReceiptHandle, cancellationToken);
                _logger.LogInformation("Ingested result for job {JobId} attempt {AttemptId}.", resultEvent.JobId, resultEvent.AttemptId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Result ingestion failed. Message will retry or go to DLQ.");
                throw;
            }
        }
    }
}
