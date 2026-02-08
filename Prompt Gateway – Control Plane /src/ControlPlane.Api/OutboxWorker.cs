using ControlPlane.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api;

public sealed class OutboxWorker : BackgroundService
{
    private readonly DispatchOutboxProcessor _processor;
    private readonly ILogger<OutboxWorker> _logger;
    private readonly OutboxWorkerOptions _options;

    public OutboxWorker(
        DispatchOutboxProcessor processor,
        ILogger<OutboxWorker> logger,
        OutboxWorkerOptions options)
    {
        _processor = processor;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await _processor.ProcessOnceAsync(stoppingToken);
                if (!processed)
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
