using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Provider.Worker.Options;
using Provider.Worker.Services;

namespace Provider.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IQueueClient _sqs;
    private readonly ProviderWorkerOptions _options;
    private readonly IProviderMessageProcessor _messageProcessor;

    public Worker(
        ILogger<Worker> logger,
        IQueueClient sqs,
        IOptions<ProviderWorkerOptions> options,
        IProviderMessageProcessor messageProcessor)
    {
        _logger = logger;
        _sqs = sqs;
        _options = options.Value;
        _messageProcessor = messageProcessor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.InputQueueUrl))
        {
            _logger.LogError("ProviderWorker: InputQueueUrl is not configured.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.OutputQueueUrl))
        {
            _logger.LogError("ProviderWorker: OutputQueueUrl is not configured.");
            return;
        }

        var semaphore = new SemaphoreSlim(_options.MaxConcurrency);
        var inFlight = new List<Task>();
        var inFlightLock = new object();

        while (!stoppingToken.IsCancellationRequested)
        {
            var request = new QueueReceiveRequest
            {
                QueueUrl = _options.InputQueueUrl,
                MaxNumberOfMessages = Math.Clamp(_options.MaxMessages, 1, 10),
                WaitTimeSeconds = _options.WaitTimeSeconds,
                VisibilityTimeoutSeconds = _options.VisibilityTimeoutSeconds
            };

            QueueReceiveResult response;
            try
            {
                response = await _sqs.ReceiveMessageAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to receive messages.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                continue;
            }

            if (response.Messages is null || response.Messages.Count == 0)
            {
                continue;
            }

            foreach (var message in response.Messages)
            {
                await semaphore.WaitAsync(stoppingToken);
                var task = ProcessMessageAsync(message, stoppingToken)
                    .ContinueWith(_ => semaphore.Release(), CancellationToken.None);
                lock (inFlightLock)
                {
                    inFlight.Add(task);
                }
            }

            lock (inFlightLock)
            {
                inFlight.RemoveAll(t => t.IsCompleted);
            }
        }

        Task[] remaining;
        lock (inFlightLock)
        {
            remaining = [.. inFlight];
        }
        await Task.WhenAll(remaining);
    }

    internal Task RunAsync(CancellationToken cancellationToken)
    {
        return ExecuteAsync(cancellationToken);
    }

    private async Task ProcessMessageAsync(QueueMessage message, CancellationToken stoppingToken)
    {
        using var visibilityCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var visibilityTask = StartVisibilityExtenderAsync(message, visibilityCts.Token);

        try
        {
            var result = await _messageProcessor.ProcessAsync(message, stoppingToken);
            if (result.ShouldAcknowledge)
            {
                await DeleteMessageAsync(message, stoppingToken);
                return;
            }

            if (result.ShouldExtendVisibilityTimeout && !string.IsNullOrWhiteSpace(message.ReceiptHandle))
            {
                await _sqs.ChangeMessageVisibilityAsync(
                    _options.InputQueueUrl,
                    message.ReceiptHandle,
                    _options.VisibilityTimeoutSeconds,
                    stoppingToken);
            }
        }
        finally
        {
            visibilityCts.Cancel();
            await visibilityTask;
        }
    }

    private Task DeleteMessageAsync(QueueMessage message, CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(message.ReceiptHandle))
        {
            return Task.CompletedTask;
        }

        return _sqs.DeleteMessageAsync(_options.InputQueueUrl, message.ReceiptHandle, stoppingToken);
    }

    private async Task StartVisibilityExtenderAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.ReceiptHandle))
        {
            return;
        }

        if (_options.VisibilityTimeoutSeconds <= 0)
        {
            return;
        }

        var delaySeconds = Math.Max(5, _options.VisibilityTimeoutSeconds / 2);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await _sqs.ChangeMessageVisibilityAsync(
                    _options.InputQueueUrl,
                    message.ReceiptHandle,
                    _options.VisibilityTimeoutSeconds,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extend message visibility.");
                return;
            }
        }
    }
}
