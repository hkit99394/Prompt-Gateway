using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using Provider.Worker.Models;
using Provider.Worker.Options;
using Provider.Worker.Services;

namespace Provider.Worker.Aws;

public class SqsResultPublisher(IAmazonSQS sqs, IOptions<ProviderWorkerOptions> options) : IResultPublisher
{
    private readonly IAmazonSQS _sqs = sqs;
    private readonly ProviderWorkerOptions _options = options.Value;
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

        await _sqs.SendMessageAsync(request, cancellationToken);
    }
}
