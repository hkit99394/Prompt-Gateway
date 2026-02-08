using Microsoft.Extensions.Logging;

namespace ControlPlane.Core;

public sealed class DispatchOutboxProcessor
{
    private readonly ILogger<DispatchOutboxProcessor> _logger;
    private readonly IOutboxStore _outboxStore;
    private readonly IDispatchQueue _dispatchQueue;

    public DispatchOutboxProcessor(
        ILogger<DispatchOutboxProcessor> logger,
        IOutboxStore outboxStore,
        IDispatchQueue dispatchQueue)
    {
        _logger = logger;
        _outboxStore = outboxStore;
        _dispatchQueue = dispatchQueue;
    }

    public async Task<bool> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var next = await _outboxStore.TryDequeueAsync(cancellationToken);
        if (next is null)
        {
            return false;
        }

        try
        {
            await _dispatchQueue.PublishAsync(next.Message, cancellationToken);
            await _outboxStore.MarkDispatchedAsync(next.OutboxId, cancellationToken);
        }
        catch
        {
            await _outboxStore.ReleaseAsync(next.OutboxId, cancellationToken);
            throw;
        }

        using (_logger.BeginScope(new Dictionary<string, object?>
               {
                   ["job_id"] = next.Message.JobId,
                   ["attempt_id"] = next.Message.AttemptId,
                   ["provider"] = next.Message.Provider
               }))
        {
            _logger.LogInformation("Dispatched outbox message {OutboxId}.", next.OutboxId);
        }

        return true;
    }
}
