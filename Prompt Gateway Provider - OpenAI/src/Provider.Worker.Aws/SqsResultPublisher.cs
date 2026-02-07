using System.Net;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Provider.Worker.Models;
using Provider.Worker.Options;
using Provider.Worker.Services;

namespace Provider.Worker.Aws;

public class SqsResultPublisher(
    IAmazonSQS sqs,
    IOptions<ProviderWorkerOptions> options,
    ILogger<SqsResultPublisher> logger) : IResultPublisher
{
    private readonly IAmazonSQS _sqs = sqs;
    private readonly ProviderWorkerOptions _options = options.Value;
    private readonly ILogger<SqsResultPublisher> _logger = logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task PublishAsync(ResultEvent resultEvent, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(resultEvent, _jsonOptions);
        var request = new SendMessageRequest
        {
            QueueUrl = _options.OutputQueueUrl,
            MessageBody = payload
        };

        if (_options.OutputQueueUrl.EndsWith(".fifo", StringComparison.OrdinalIgnoreCase))
        {
            request.MessageGroupId = resultEvent.JobId;
            request.MessageDeduplicationId = $"{resultEvent.JobId}:{resultEvent.AttemptId}";
        }

        var attempt = 0;
        var maxAttempts = 3;
        while (true)
        {
            attempt++;
            try
            {
                await _sqs.SendMessageAsync(request, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts &&
                                       ShouldRetry(ex) &&
                                       !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to publish result. Retrying.");
                await Task.Delay(BackoffDelay(attempt), cancellationToken);
            }
        }
    }

    private static bool ShouldRetry(Exception ex)
    {
        if (ex is AmazonServiceException serviceException)
        {
            return serviceException.StatusCode == HttpStatusCode.TooManyRequests
                   || serviceException.StatusCode == HttpStatusCode.RequestTimeout
                   || serviceException.StatusCode == HttpStatusCode.BadGateway
                   || serviceException.StatusCode == HttpStatusCode.ServiceUnavailable
                   || serviceException.StatusCode == HttpStatusCode.GatewayTimeout
                   || serviceException.StatusCode == HttpStatusCode.InternalServerError;
        }

        return false;
    }

    private static TimeSpan BackoffDelay(int attempt)
    {
        var jitter = Random.Shared.NextDouble() * 0.3 + 0.85;
        var seconds = Math.Pow(2, attempt) * jitter;
        return TimeSpan.FromSeconds(Math.Min(10, seconds));
    }
}
