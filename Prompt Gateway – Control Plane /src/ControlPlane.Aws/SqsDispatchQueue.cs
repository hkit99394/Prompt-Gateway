using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using ControlPlane.Core;

namespace ControlPlane.Aws;

public sealed class SqsDispatchQueue : IDispatchQueue
{
    private readonly IAmazonSQS _sqs;
    private readonly AwsQueueOptions _options;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SqsDispatchQueue(IAmazonSQS sqs, AwsQueueOptions options)
    {
        _sqs = sqs;
        _options = options;
    }

    public async Task PublishAsync(DispatchMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.DispatchQueueUrl))
        {
            throw new InvalidOperationException("DispatchQueueUrl is not configured.");
        }

        var body = JsonSerializer.Serialize(message, _serializerOptions);
        var request = new SendMessageRequest
        {
            QueueUrl = _options.DispatchQueueUrl,
            MessageBody = body,
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["job_id"] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = message.JobId
                },
                ["attempt_id"] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = message.AttemptId
                },
                ["provider"] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = message.Provider
                }
            }
        };

        await _sqs.SendMessageAsync(request, cancellationToken);
    }
}
