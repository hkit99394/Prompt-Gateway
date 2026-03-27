using ControlPlane.Api;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ControlPlane.Core.Tests;

public class OutboxWorkerTests
{
    [Test]
    public async Task StartAsync_UsesConfiguredBatchLimit()
    {
        var batchProcessor = Substitute.For<IOutboxDispatchBatchProcessor>();
        var logger = Substitute.For<ILogger<OutboxWorker>>();
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        batchProcessor.ProcessAsync(7, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                invoked.TrySetResult();
                return Task.FromResult(new OutboxDispatchBatchResult(0, reachedLimit: false));
            });

        var worker = new OutboxWorker(
            batchProcessor,
            logger,
            new OutboxWorkerOptions
            {
                MaxMessagesPerCycle = 7,
                IdleDelay = TimeSpan.FromSeconds(10),
                ErrorDelay = TimeSpan.FromSeconds(10)
            });

        await worker.StartAsync(CancellationToken.None);
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        await batchProcessor.Received(1).ProcessAsync(7, Arg.Any<CancellationToken>());
    }
}
