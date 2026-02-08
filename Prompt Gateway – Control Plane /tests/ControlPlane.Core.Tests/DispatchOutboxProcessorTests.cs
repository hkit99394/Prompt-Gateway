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

    [Test]
    public async Task ProcessOnceAsync_ReleasesWhenPublishFails()
    {
        var logger = Substitute.For<ILogger<DispatchOutboxProcessor>>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var dispatchQueue = Substitute.For<IDispatchQueue>();

        var message = new DispatchMessage
        {
            JobId = "job-8",
            AttemptId = "attempt-8",
            Provider = "openai",
            Model = "gpt-4.1",
            TraceId = "trace-8",
            IdempotencyKey = "job-8:attempt-8",
            Request = new CanonicalJobRequest
            {
                JobId = "job-8",
                AttemptId = "attempt-8",
                TraceId = "trace-8",
                TaskType = "chat_completion"
            }
        };

        outboxStore.TryDequeueAsync(Arg.Any<CancellationToken>())
            .Returns(new OutboxDispatchMessage("outbox-8", message, DateTimeOffset.UtcNow));
        dispatchQueue.PublishAsync(message, Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("publish failed"));

        var processor = new DispatchOutboxProcessor(logger, outboxStore, dispatchQueue);

        Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessOnceAsync(CancellationToken.None));
        await outboxStore.Received(1).ReleaseAsync("outbox-8", Arg.Any<CancellationToken>());
        await outboxStore.DidNotReceive().MarkDispatchedAsync("outbox-8", Arg.Any<CancellationToken>());
    }
}
