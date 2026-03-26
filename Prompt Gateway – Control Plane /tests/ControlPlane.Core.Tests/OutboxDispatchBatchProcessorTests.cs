using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ControlPlane.Core.Tests;

public class OutboxDispatchBatchProcessorTests
{
    [Test]
    public async Task ProcessAsync_StopsWhenOutboxIsEmpty()
    {
        var logger = Substitute.For<ILogger<DispatchOutboxProcessor>>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var dispatchQueue = Substitute.For<IDispatchQueue>();

        outboxStore.TryDequeueAsync(Arg.Any<CancellationToken>())
            .Returns(
                new OutboxDispatchMessage("outbox-1", CreateMessage("job-1", "attempt-1"), DateTimeOffset.UtcNow),
                new OutboxDispatchMessage("outbox-2", CreateMessage("job-2", "attempt-2"), DateTimeOffset.UtcNow),
                null as OutboxDispatchMessage);

        var processor = new DispatchOutboxProcessor(logger, outboxStore, dispatchQueue);
        var batchProcessor = new OutboxDispatchBatchProcessor(processor);

        var result = await batchProcessor.ProcessAsync(10, CancellationToken.None);

        Assert.That(result.ProcessedCount, Is.EqualTo(2));
        Assert.That(result.ReachedLimit, Is.False);
    }

    [Test]
    public async Task ProcessAsync_StopsAtConfiguredLimit()
    {
        var logger = Substitute.For<ILogger<DispatchOutboxProcessor>>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var dispatchQueue = Substitute.For<IDispatchQueue>();

        outboxStore.TryDequeueAsync(Arg.Any<CancellationToken>())
            .Returns(
                new OutboxDispatchMessage("outbox-1", CreateMessage("job-1", "attempt-1"), DateTimeOffset.UtcNow),
                new OutboxDispatchMessage("outbox-2", CreateMessage("job-2", "attempt-2"), DateTimeOffset.UtcNow),
                new OutboxDispatchMessage("outbox-3", CreateMessage("job-3", "attempt-3"), DateTimeOffset.UtcNow));

        var processor = new DispatchOutboxProcessor(logger, outboxStore, dispatchQueue);
        var batchProcessor = new OutboxDispatchBatchProcessor(processor);

        var result = await batchProcessor.ProcessAsync(2, CancellationToken.None);

        Assert.That(result.ProcessedCount, Is.EqualTo(2));
        Assert.That(result.ReachedLimit, Is.True);
        await outboxStore.Received(2).MarkDispatchedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void ProcessAsync_RejectsNonPositiveLimits()
    {
        var logger = Substitute.For<ILogger<DispatchOutboxProcessor>>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var dispatchQueue = Substitute.For<IDispatchQueue>();
        var processor = new DispatchOutboxProcessor(logger, outboxStore, dispatchQueue);
        var batchProcessor = new OutboxDispatchBatchProcessor(processor);

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => batchProcessor.ProcessAsync(0, CancellationToken.None));
    }

    private static DispatchMessage CreateMessage(string jobId, string attemptId)
    {
        return new DispatchMessage
        {
            JobId = jobId,
            AttemptId = attemptId,
            Provider = "openai",
            Model = "gpt-4.1",
            TraceId = $"trace-{jobId}",
            IdempotencyKey = $"{jobId}:{attemptId}",
            Request = new CanonicalJobRequest
            {
                JobId = jobId,
                AttemptId = attemptId,
                TraceId = $"trace-{jobId}",
                TaskType = "chat_completion"
            }
        };
    }
}
