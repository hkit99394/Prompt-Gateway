namespace ControlPlane.Core;

public interface IOutboxDispatchBatchProcessor
{
    Task<OutboxDispatchBatchResult> ProcessAsync(int maxMessages, CancellationToken cancellationToken);
}

public sealed class OutboxDispatchBatchResult
{
    public OutboxDispatchBatchResult(int processedCount, bool reachedLimit)
    {
        ProcessedCount = processedCount;
        ReachedLimit = reachedLimit;
    }

    public int ProcessedCount { get; }

    public bool ReachedLimit { get; }
}

public sealed class OutboxDispatchBatchProcessor : IOutboxDispatchBatchProcessor
{
    private readonly DispatchOutboxProcessor _processor;

    public OutboxDispatchBatchProcessor(DispatchOutboxProcessor processor)
    {
        _processor = processor;
    }

    public async Task<OutboxDispatchBatchResult> ProcessAsync(int maxMessages, CancellationToken cancellationToken)
    {
        if (maxMessages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessages), "maxMessages must be greater than zero.");
        }

        var processedCount = 0;
        for (; processedCount < maxMessages; processedCount++)
        {
            var processed = await _processor.ProcessOnceAsync(cancellationToken);
            if (!processed)
            {
                return new OutboxDispatchBatchResult(processedCount, reachedLimit: false);
            }
        }

        return new OutboxDispatchBatchResult(processedCount, reachedLimit: true);
    }
}
