using Microsoft.Extensions.Logging;
using NSubstitute;
using Provider.Worker.Lambda;
using Provider.Worker.Services;

namespace Provider.Worker.Tests;

public class ProviderDispatchFunctionTests
{
    [Test]
    public async Task FunctionHandlerAsync_ReturnsNoFailuresWhenAllMessagesAck()
    {
        var processor = Substitute.For<IProviderMessageProcessor>();
        var logger = Substitute.For<ILogger<ProviderDispatchFunction>>();
        processor.ProcessAsync(Arg.Any<QueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(ProviderMessageProcessResult.Acknowledge());

        var function = new ProviderDispatchFunction(processor, logger);

        var response = await function.FunctionHandlerAsync(new SqsEvent
        {
            Records =
            [
                new SqsMessage { MessageId = "m1", Body = "body-1" },
                new SqsMessage { MessageId = "m2", Body = "body-2" }
            ]
        }, CancellationToken.None);

        Assert.That(response.BatchItemFailures, Is.Empty);
        await processor.Received(2).ProcessAsync(Arg.Any<QueueMessage>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FunctionHandlerAsync_ReturnsFailureForRetryMessage()
    {
        var processor = Substitute.For<IProviderMessageProcessor>();
        var logger = Substitute.For<ILogger<ProviderDispatchFunction>>();
        processor.ProcessAsync(Arg.Any<QueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(ProviderMessageProcessResult.Retry());

        var function = new ProviderDispatchFunction(processor, logger);

        var response = await function.FunctionHandlerAsync(new SqsEvent
        {
            Records = [new SqsMessage { MessageId = "m1", Body = "body-1" }]
        }, CancellationToken.None);

        Assert.That(response.BatchItemFailures, Has.Count.EqualTo(1));
        Assert.That(response.BatchItemFailures[0].ItemIdentifier, Is.EqualTo("m1"));
    }

    [Test]
    public async Task FunctionHandlerAsync_ContinuesAfterExceptionAndMarksOnlyFailedRecord()
    {
        var processor = Substitute.For<IProviderMessageProcessor>();
        var logger = Substitute.For<ILogger<ProviderDispatchFunction>>();
        processor.ProcessAsync(Arg.Any<QueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<ProviderMessageProcessResult>(new InvalidOperationException("boom")),
                _ => Task.FromResult(ProviderMessageProcessResult.Acknowledge()));

        var function = new ProviderDispatchFunction(processor, logger);

        var response = await function.FunctionHandlerAsync(new SqsEvent
        {
            Records =
            [
                new SqsMessage { MessageId = "m1", Body = "body-1" },
                new SqsMessage { MessageId = "m2", Body = "body-2" }
            ]
        }, CancellationToken.None);

        Assert.That(response.BatchItemFailures, Has.Count.EqualTo(1));
        Assert.That(response.BatchItemFailures[0].ItemIdentifier, Is.EqualTo("m1"));
        await processor.Received(2).ProcessAsync(Arg.Any<QueueMessage>(), Arg.Any<CancellationToken>());
    }
}
