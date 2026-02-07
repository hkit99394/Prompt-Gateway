using System.Text.Json;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using Provider.Worker.Models;
using Provider.Worker.Options;
using Provider.Worker.Services;

namespace Provider.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ISqsClient _sqs;
    private readonly ProviderWorkerOptions _options;
    private readonly IDedupeStore _dedupeStore;
    private readonly IPromptLoader _promptLoader;
    private readonly IOpenAiClient _openAiClient;
    private readonly IResultPayloadStore _payloadStore;
    private readonly IResultPublisher _publisher;
    private readonly JsonSerializerOptions _jsonOptions;

    public Worker(
        ILogger<Worker> logger,
        ISqsClient sqs,
        IOptions<ProviderWorkerOptions> options,
        IDedupeStore dedupeStore,
        IPromptLoader promptLoader,
        IOpenAiClient openAiClient,
        IResultPayloadStore payloadStore,
        IResultPublisher publisher)
    {
        _logger = logger;
        _sqs = sqs;
        _options = options.Value;
        _dedupeStore = dedupeStore;
        _promptLoader = promptLoader;
        _openAiClient = openAiClient;
        _payloadStore = payloadStore;
        _publisher = publisher;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
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
            var request = new ReceiveMessageRequest
            {
                QueueUrl = _options.InputQueueUrl,
                MaxNumberOfMessages = Math.Clamp(_options.MaxMessages, 1, 10),
                WaitTimeSeconds = _options.WaitTimeSeconds,
                VisibilityTimeout = _options.VisibilityTimeoutSeconds
            };

            ReceiveMessageResponse response;
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
            remaining = inFlight.ToArray();
        }
        await Task.WhenAll(remaining);
    }

    internal Task RunAsync(CancellationToken cancellationToken)
    {
        return ExecuteAsync(cancellationToken);
    }

    private async Task ProcessMessageAsync(Message message, CancellationToken stoppingToken)
    {
        CanonicalJobRequest? job;
        try
        {
            job = JsonSerializer.Deserialize<CanonicalJobRequest>(message.Body, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize message.");
            await DeleteMessageAsync(message, stoppingToken);
            return;
        }

        if (job is null || string.IsNullOrWhiteSpace(job.JobId) || string.IsNullOrWhiteSpace(job.AttemptId))
        {
            _logger.LogWarning("Invalid job payload. Deleting message.");
            await DeleteMessageAsync(message, stoppingToken);
            return;
        }

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["job_id"] = job.JobId,
            ["attempt_id"] = job.AttemptId,
            ["provider"] = _options.ProviderName,
            ["model"] = job.Model ?? _options.OpenAi.Model
        });

        if (!await _dedupeStore.TryStartAsync(job.JobId, job.AttemptId, stoppingToken))
        {
            _logger.LogInformation("Duplicate job detected. Skipping.");
            await DeleteMessageAsync(message, stoppingToken);
            return;
        }

        if (!string.Equals(job.TaskType, CanonicalTaskTypes.ChatCompletion, StringComparison.OrdinalIgnoreCase))
        {
            await PublishErrorAsync(
                job,
                CanonicalError.Create("unsupported_task", $"Unsupported task type: {job.TaskType}"),
                stoppingToken);
            await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, stoppingToken);
            await DeleteMessageAsync(message, stoppingToken);
            return;
        }

        string promptText;
        try
        {
            promptText = await _promptLoader.LoadPromptAsync(job, stoppingToken);
        }
        catch (Exception ex)
        {
            await PublishErrorAsync(
                job,
                CanonicalError.Create("prompt_load_failed", ex.Message),
                stoppingToken);
            await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, stoppingToken);
            await DeleteMessageAsync(message, stoppingToken);
            return;
        }

        using var visibilityCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var visibilityTask = StartVisibilityExtenderAsync(message, visibilityCts.Token);

        OpenAiResult? openAiResult;
        try
        {
            openAiResult = await _openAiClient.ExecuteAsync(job, promptText, stoppingToken);
        }
        catch (OpenAiException ex)
        {
            var error = CanonicalError.FromOpenAi(ex);
            await PublishErrorAsync(job, error, stoppingToken, ex.RawPayload);
            await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, stoppingToken);
            await DeleteMessageAsync(message, stoppingToken);
            return;
        }
        catch (Exception ex)
        {
            await PublishErrorAsync(job, CanonicalError.Create("provider_error", ex.Message), stoppingToken);
            await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, stoppingToken);
            await DeleteMessageAsync(message, stoppingToken);
            return;
        }
        finally
        {
            visibilityCts.Cancel();
            await visibilityTask;
        }

        var responsePayload = new CanonicalResponse
        {
            OutputText = openAiResult.Content,
            Model = openAiResult.Model,
            FinishReason = openAiResult.FinishReason,
            Usage = openAiResult.Usage
        };

        var rawResponseJson = openAiResult.RawJson;
        if (!string.IsNullOrWhiteSpace(rawResponseJson))
        {
            responsePayload.RawPayloadReference = await _payloadStore.StoreIfLargeAsync(
                job,
                rawResponseJson,
                stoppingToken);
        }

        var resultEvent = ResultEvent.Success(job, responsePayload);
        resultEvent.Provider = _options.ProviderName;
        await _publisher.PublishAsync(resultEvent, stoppingToken);
        await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, stoppingToken);
        await DeleteMessageAsync(message, stoppingToken);
    }

    private async Task PublishErrorAsync(
        CanonicalJobRequest job,
        CanonicalError error,
        CancellationToken stoppingToken,
        string? rawPayload = null)
    {
        if (!string.IsNullOrWhiteSpace(rawPayload))
        {
            error.RawPayloadReference = await _payloadStore.StoreAsync(
                job,
                rawPayload,
                "error.json",
                stoppingToken);
        }

        var resultEvent = ResultEvent.Failure(job, error);
        resultEvent.Provider = _options.ProviderName;
        await _publisher.PublishAsync(resultEvent, stoppingToken);
    }

    private Task DeleteMessageAsync(Message message, CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(message.ReceiptHandle))
        {
            return Task.CompletedTask;
        }

        return _sqs.DeleteMessageAsync(_options.InputQueueUrl, message.ReceiptHandle, stoppingToken);
    }

    private async Task StartVisibilityExtenderAsync(Message message, CancellationToken cancellationToken)
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
            }
        }
    }
}
