using System.Net;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Provider.Worker.Aws;
using Provider.Worker.Models;
using Provider.Worker.Options;

namespace Provider.Worker.Tests;

public class SqsResultPublisherTests
{
    [Test]
    public async Task PublishAsync_RetriesOnTransientError()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var logger = Substitute.For<ILogger<SqsResultPublisher>>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            OutputQueueUrl = "https://example.com/queue.fifo"
        });

        var transient = new AmazonServiceException("transient")
        {
            StatusCode = HttpStatusCode.InternalServerError
        };

        sqs.SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<SendMessageResponse>(transient),
                _ => Task.FromResult(new SendMessageResponse()));

        var publisher = new SqsResultPublisher(sqs, options, logger);
        var resultEvent = ResultEvent.Success(new CanonicalJobRequest
        {
            JobId = "job-1",
            AttemptId = "attempt-1"
        }, new CanonicalResponse());

        await publisher.PublishAsync(resultEvent, CancellationToken.None);

        await sqs.Received(2).SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PublishAsync_SetsFifoFields()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var logger = Substitute.For<ILogger<SqsResultPublisher>>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            OutputQueueUrl = "https://example.com/queue.fifo"
        });

        SendMessageRequest? captured = null;
        sqs.SendMessageAsync(Arg.Do<SendMessageRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new SendMessageResponse()));

        var publisher = new SqsResultPublisher(sqs, options, logger);
        var resultEvent = ResultEvent.Success(new CanonicalJobRequest
        {
            JobId = "job-2",
            AttemptId = "attempt-2"
        }, new CanonicalResponse());

        await publisher.PublishAsync(resultEvent, CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.MessageGroupId, Is.EqualTo("job-2"));
        Assert.That(captured.MessageDeduplicationId, Is.EqualTo("job-2:attempt-2"));
    }

    [Test]
    public void PublishAsync_DoesNotRetryOnNonTransientError()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var logger = Substitute.For<ILogger<SqsResultPublisher>>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            OutputQueueUrl = "https://example.com/queue"
        });

        var nonTransient = new AmazonServiceException("forbidden")
        {
            StatusCode = HttpStatusCode.Forbidden
        };

        sqs.SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<SendMessageResponse>(nonTransient));

        var publisher = new SqsResultPublisher(sqs, options, logger);
        var resultEvent = ResultEvent.Success(new CanonicalJobRequest
        {
            JobId = "job-3",
            AttemptId = "attempt-3"
        }, new CanonicalResponse());

        Assert.ThrowsAsync<AmazonServiceException>(async () =>
            await publisher.PublishAsync(resultEvent, CancellationToken.None));
        sqs.Received(1).SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>());
    }
}
