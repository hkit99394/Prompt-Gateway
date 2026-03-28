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
    private readonly IResultMessageProcessor _messageProcessor;

    public ResultQueueProcessor(
        IAmazonSQS sqs,
        AwsQueueOptions options,
        IResultMessageProcessor messageProcessor)
    {
        _sqs = sqs;
        _options = options;
        _messageProcessor = messageProcessor;
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
        var result = await _messageProcessor.ProcessAsync(
            message.Body ?? string.Empty,
            message.MessageId,
            cancellationToken);
        if (result.ShouldAcknowledge)
        {
            await _sqs.DeleteMessageAsync(_options.ResultQueueUrl, message.ReceiptHandle, cancellationToken);
            return true;
        }

        throw new InvalidOperationException("Result ingestion failed. Message will retry or go to DLQ.");
    }
}
