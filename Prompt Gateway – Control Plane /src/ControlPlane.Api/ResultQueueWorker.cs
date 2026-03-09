using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api;

public sealed class ResultQueueWorker : BackgroundService
{
    private readonly ResultQueueProcessor _processor;
    private readonly ILogger<ResultQueueWorker> _logger;
    private readonly ResultQueueWorkerOptions _options;

    public ResultQueueWorker(
        ResultQueueProcessor processor,
        ILogger<ResultQueueWorker> logger,
        ResultQueueWorkerOptions options)
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
                    await Task.Delay(_options.IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Result queue worker failed.");
                await Task.Delay(_options.ErrorDelay, stoppingToken);
            }
        }
    }
}
