using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ControlPlane.Core.Tests;

public class DispatchOutboxProcessorTests
{
    [Test]
    public async Task ProcessOnceAsync_PublishesAndMarksDispatched()
    {
        var logger = Substitute.For<ILogger<DispatchOutboxProcessor>>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var dispatchQueue = Substitute.For<IDispatchQueue>();

        var message = new DispatchMessage
        {
            JobId = "job-7",
            AttemptId = "attempt-7",
            Provider = "openai",
            Model = "gpt-4.1",
            TraceId = "trace-7",
            IdempotencyKey = "job-7:attempt-7",
            Request = new CanonicalJobRequest
            {
                JobId = "job-7",
                AttemptId = "attempt-7",
                TraceId = "trace-7",
                TaskType = "chat_completion"
            }
        };

        outboxStore.TryDequeueAsync(Arg.Any<CancellationToken>())
            .Returns(new OutboxDispatchMessage("outbox-7", message, DateTimeOffset.UtcNow));

        var processor = new DispatchOutboxProcessor(logger, outboxStore, dispatchQueue);

        var processed = await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.That(processed, Is.True);
        await dispatchQueue.Received(1).PublishAsync(message, Arg.Any<CancellationToken>());
        await outboxStore.Received(1).MarkDispatchedAsync("outbox-7", Arg.Any<CancellationToken>());
    }
}
