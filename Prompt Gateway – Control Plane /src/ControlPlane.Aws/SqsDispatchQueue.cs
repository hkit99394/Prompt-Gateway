using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using ControlPlane.Core;

namespace ControlPlane.Aws;

public sealed class SqsDispatchQueue : IDispatchQueue
{
    private readonly IAmazonSQS _sqs;
    private readonly AwsQueueOptions _options;

    /// <summary>Snake_case so the Provider Worker can deserialize (expects job_id, attempt_id, task_type, etc.).</summary>
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = new SnakeCaseNamingPolicy()
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

        // Worker expects CanonicalJobRequest with snake_case (job_id, attempt_id, task_type). Send message.Request, not the full DispatchMessage.
        var body = JsonSerializer.Serialize(message.Request, SnakeCaseOptions);
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
