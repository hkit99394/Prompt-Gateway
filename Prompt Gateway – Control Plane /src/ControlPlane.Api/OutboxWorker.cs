using ControlPlane.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api;

public sealed class OutboxWorker : BackgroundService
{
    private readonly IOutboxDispatchBatchProcessor _batchProcessor;
    private readonly ILogger<OutboxWorker> _logger;
    private readonly OutboxWorkerOptions _options;

    public OutboxWorker(
        IOutboxDispatchBatchProcessor batchProcessor,
        ILogger<OutboxWorker> logger,
        OutboxWorkerOptions options)
    {
        _batchProcessor = batchProcessor;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _batchProcessor.ProcessAsync(_options.MaxMessagesPerCycle, stoppingToken);
                if (result.ProcessedCount == 0)
                {
                    await Task.Delay(_options.IdleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox worker failed.");
                await Task.Delay(_options.ErrorDelay, stoppingToken);
            }
        }
    }
}
