using ControlPlane.OutboxLambda;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ControlPlane.Core.Tests;

public class OutboxDispatchFunctionTests
{
    [Test]
    public async Task FunctionHandlerAsync_ReturnsInvocationSummary()
    {
        var batchProcessor = Substitute.For<IOutboxDispatchBatchProcessor>();
        var logger = Substitute.For<ILogger<OutboxDispatchFunction>>();
        batchProcessor.ProcessAsync(25, Arg.Any<CancellationToken>())
            .Returns(new OutboxDispatchBatchResult(7, reachedLimit: false));

        var function = new OutboxDispatchFunction(
            batchProcessor,
            logger,
            new OutboxLambdaOptions { MaxMessagesPerInvocation = 25 });

        var result = await function.FunctionHandlerAsync(CancellationToken.None);

        Assert.That(result.ProcessedCount, Is.EqualTo(7));
        Assert.That(result.ReachedLimit, Is.False);
    }

    [Test]
    public void FunctionHandlerAsync_PropagatesBatchFailures()
    {
        var batchProcessor = Substitute.For<IOutboxDispatchBatchProcessor>();
        var logger = Substitute.For<ILogger<OutboxDispatchFunction>>();
        batchProcessor.ProcessAsync(10, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<OutboxDispatchBatchResult>(new InvalidOperationException("boom")));

        var function = new OutboxDispatchFunction(
            batchProcessor,
            logger,
            new OutboxLambdaOptions { MaxMessagesPerInvocation = 10 });

        Assert.ThrowsAsync<InvalidOperationException>(() => function.FunctionHandlerAsync(CancellationToken.None));
    }
}
