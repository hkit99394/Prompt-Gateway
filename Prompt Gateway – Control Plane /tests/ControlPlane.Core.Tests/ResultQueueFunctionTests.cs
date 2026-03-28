using ControlPlane.ResultLambda;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ControlPlane.Core.Tests;

public class ResultQueueFunctionTests
{
    [Test]
    public async Task FunctionHandlerAsync_ReturnsNoFailuresWhenAllMessagesAck()
    {
        var processor = Substitute.For<IResultMessageProcessor>();
        var logger = Substitute.For<ILogger<ResultQueueFunction>>();
        processor.ProcessAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ResultMessageProcessResult.Acknowledge());

        var function = new ResultQueueFunction(processor, logger);

        var response = await function.FunctionHandlerAsync(new SqsEvent
        {
            Records =
            [
                new SqsMessage { MessageId = "m1", Body = "body-1" },
                new SqsMessage { MessageId = "m2", Body = "body-2" }
            ]
        }, CancellationToken.None);

        Assert.That(response.BatchItemFailures, Is.Empty);
    }

    [Test]
    public async Task FunctionHandlerAsync_ReturnsFailureForRetryMessage()
    {
        var processor = Substitute.For<IResultMessageProcessor>();
        var logger = Substitute.For<ILogger<ResultQueueFunction>>();
        processor.ProcessAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ResultMessageProcessResult.Retry());

        var function = new ResultQueueFunction(processor, logger);

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
        var processor = Substitute.For<IResultMessageProcessor>();
        var logger = Substitute.For<ILogger<ResultQueueFunction>>();
        processor.ProcessAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<ResultMessageProcessResult>(new InvalidOperationException("boom")),
                _ => Task.FromResult(ResultMessageProcessResult.Acknowledge()));

        var function = new ResultQueueFunction(processor, logger);

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
    }
}
