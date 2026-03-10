using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using ControlPlane.Aws;
using ControlPlane.Core;
using NSubstitute;

namespace ControlPlane.Core.Tests;

public class SqsDispatchQueueTests
{
    [Test]
    public async Task PublishAsync_MapsInputRefToPromptS3Key()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        SendMessageRequest? captured = null;
        sqs.SendMessageAsync(Arg.Do<SendMessageRequest>(request => captured = request), Arg.Any<CancellationToken>())
            .Returns(new SendMessageResponse());

        var dispatchQueue = new SqsDispatchQueue(sqs, new AwsQueueOptions
        {
            DispatchQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/dispatch"
        });

        var message = new DispatchMessage
        {
            JobId = "job-1",
            AttemptId = "attempt-1",
            TraceId = "trace-1",
            Provider = "openai",
            Model = "gpt-4.1",
            IdempotencyKey = "job-1:attempt-1",
            Request = new CanonicalJobRequest
            {
                JobId = "job-1",
                AttemptId = "attempt-1",
                TraceId = "trace-1",
                TaskType = "chat_completion",
                InputRef = "prompts/job-1.txt"
            }
        };

        await dispatchQueue.PublishAsync(message, CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        using var body = JsonDocument.Parse(captured!.MessageBody);
        var root = body.RootElement;
        Assert.That(root.GetProperty("job_id").GetString(), Is.EqualTo("job-1"));
        Assert.That(root.GetProperty("attempt_id").GetString(), Is.EqualTo("attempt-1"));
        Assert.That(root.GetProperty("task_type").GetString(), Is.EqualTo("chat_completion"));
        Assert.That(root.GetProperty("prompt_s3_key").GetString(), Is.EqualTo("prompts/job-1.txt"));
    }

    [Test]
    public async Task PublishAsync_ParsesS3InputRefToBucketAndKey()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        SendMessageRequest? captured = null;
        sqs.SendMessageAsync(Arg.Do<SendMessageRequest>(request => captured = request), Arg.Any<CancellationToken>())
            .Returns(new SendMessageResponse());

        var dispatchQueue = new SqsDispatchQueue(sqs, new AwsQueueOptions
        {
            DispatchQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/dispatch"
        });

        var message = new DispatchMessage
        {
            JobId = "job-2",
            AttemptId = "attempt-2",
            TraceId = "trace-2",
            Provider = "openai",
            Model = "gpt-4.1",
            IdempotencyKey = "job-2:attempt-2",
            Request = new CanonicalJobRequest
            {
                JobId = "job-2",
                AttemptId = "attempt-2",
                TraceId = "trace-2",
                TaskType = "chat_completion",
                InputRef = "s3://prompt-bucket/prompts/job-2.txt"
            }
        };

        await dispatchQueue.PublishAsync(message, CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        using var body = JsonDocument.Parse(captured!.MessageBody);
        var root = body.RootElement;
        Assert.That(root.GetProperty("prompt_s3_bucket").GetString(), Is.EqualTo("prompt-bucket"));
        Assert.That(root.GetProperty("prompt_s3_key").GetString(), Is.EqualTo("prompts/job-2.txt"));
    }
}
