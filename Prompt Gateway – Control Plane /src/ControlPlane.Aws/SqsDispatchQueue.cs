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

        var workerRequest = MapToWorkerRequest(message);
        var body = JsonSerializer.Serialize(workerRequest, SnakeCaseOptions);
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

    private static WorkerDispatchRequest MapToWorkerRequest(DispatchMessage message)
    {
        var request = message.Request;
        var promptKey = request.PromptKey;
        var promptBucket = request.PromptBucket;
        var promptS3Key = request.PromptS3Key;
        var promptS3Bucket = request.PromptS3Bucket;

        if (string.IsNullOrWhiteSpace(promptKey)
            && string.IsNullOrWhiteSpace(promptS3Key)
            && !string.IsNullOrWhiteSpace(request.InputRef))
        {
            if (TryParseS3Uri(request.InputRef!, out var parsedBucket, out var parsedKey))
            {
                promptS3Bucket ??= parsedBucket;
                promptS3Key ??= parsedKey;
            }
            else
            {
                promptS3Key = request.InputRef;
            }
        }

        return new WorkerDispatchRequest
        {
            JobId = message.JobId,
            AttemptId = message.AttemptId,
            TraceId = message.TraceId,
            TaskType = request.TaskType,
            PromptText = request.PromptText,
            PromptKey = promptKey,
            PromptBucket = promptBucket,
            PromptS3Key = promptS3Key,
            PromptS3Bucket = promptS3Bucket,
            SystemPrompt = request.SystemPrompt,
            Model = string.IsNullOrWhiteSpace(request.Model) ? message.Model : request.Model,
            PromptInput = request.PromptInput,
            PromptVariables = request.PromptVariables,
            Metadata = request.Metadata,
            InputRef = request.InputRef
        };
    }

    private static bool TryParseS3Uri(string inputRef, out string bucket, out string key)
    {
        bucket = string.Empty;
        key = string.Empty;

        if (!Uri.TryCreate(inputRef, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, "s3", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        var parsedKey = uri.AbsolutePath.TrimStart('/');
        if (string.IsNullOrWhiteSpace(parsedKey))
        {
            return false;
        }

        bucket = uri.Host;
        key = parsedKey;
        return true;
    }

    private sealed class WorkerDispatchRequest
    {
        public string JobId { get; init; } = string.Empty;
        public string AttemptId { get; init; } = string.Empty;
        public string TraceId { get; init; } = string.Empty;
        public string TaskType { get; init; } = string.Empty;
        public string? PromptText { get; init; }
        public string? PromptKey { get; init; }
        public string? PromptBucket { get; init; }
        public string? PromptS3Key { get; init; }
        public string? PromptS3Bucket { get; init; }
        public string? SystemPrompt { get; init; }
        public string? Model { get; init; }
        public string? PromptInput { get; init; }
        public Dictionary<string, string>? PromptVariables { get; init; }
        public Dictionary<string, string>? Metadata { get; init; }
        public string? InputRef { get; init; }
    }
}
