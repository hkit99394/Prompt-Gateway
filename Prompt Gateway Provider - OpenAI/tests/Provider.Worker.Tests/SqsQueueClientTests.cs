using Amazon.SQS;
using Amazon.SQS.Model;
using NSubstitute;
using Provider.Worker.Aws;
using Provider.Worker.Services;

namespace Provider.Worker.Tests;

public class SqsQueueClientTests
{
    [Test]
    public async Task ReceiveMessageAsync_ReturnsEmptyWhenNoMessages()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var client = new SqsQueueClient(sqs);

        sqs.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ReceiveMessageResponse { Messages = null });

        var result = await client.ReceiveMessageAsync(new QueueReceiveRequest
        {
            QueueUrl = "queue",
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 0,
            VisibilityTimeoutSeconds = 0
        }, CancellationToken.None);

        Assert.That(result.Messages, Is.Empty);
    }

    [Test]
    public async Task ReceiveMessageAsync_MapsMessages()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var client = new SqsQueueClient(sqs);

        sqs.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ReceiveMessageResponse
            {
                Messages = new List<Message>
                {
                    new()
                    {
                        Body = "body",
                        ReceiptHandle = "rh-1"
                    }
                }
            });

        var result = await client.ReceiveMessageAsync(new QueueReceiveRequest
        {
            QueueUrl = "queue",
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 0,
            VisibilityTimeoutSeconds = 0
        }, CancellationToken.None);

        Assert.That(result.Messages, Has.Count.EqualTo(1));
        Assert.That(result.Messages[0].Body, Is.EqualTo("body"));
        Assert.That(result.Messages[0].ReceiptHandle, Is.EqualTo("rh-1"));
    }
}
