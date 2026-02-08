using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Provider.Worker.Models;
using Provider.Worker.Options;
using Provider.Worker.Services;

namespace Provider.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IQueueClient _sqs;
    private readonly ProviderWorkerOptions _options;
    private readonly IDedupeStore _dedupeStore;
    private readonly IPromptTemplateStore _templateStore;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IOpenAiClient _openAiClient;
    private readonly IResultPayloadStore _payloadStore;
    private readonly IResultPublisher _publisher;
    private readonly JsonSerializerOptions _jsonOptions;

    public Worker(
        ILogger<Worker> logger,
        IQueueClient sqs,
        IOptions<ProviderWorkerOptions> options,
        IDedupeStore dedupeStore,
        IPromptTemplateStore templateStore,
        IPromptBuilder promptBuilder,
        IOpenAiClient openAiClient,
        IResultPayloadStore payloadStore,
        IResultPublisher publisher)
    {
        _logger = logger;
        _sqs = sqs;
        _options = options.Value;
        _dedupeStore = dedupeStore;
        _templateStore = templateStore;
        _promptBuilder = promptBuilder;
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
        if (string.IsNullOrWhiteSpace(message.Body))
        {
            _logger.LogWarning("Empty message body. Deleting message.");
            await DeleteMessageAsync(message, stoppingToken);
            return;
        }

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
            ["model"] = string.IsNullOrWhiteSpace(job.Model) ? _options.OpenAi.Model : job.Model
        });

        var dedupeDecision = await _dedupeStore.TryStartAsync(job.JobId, job.AttemptId, stoppingToken);
        if (dedupeDecision == DedupeDecision.DuplicateCompleted)
        {
            _logger.LogInformation("Duplicate completed job detected. Deleting message.");
            await DeleteMessageAsync(message, stoppingToken);
            return;
        }

        if (dedupeDecision == DedupeDecision.DuplicateInProgress)
        {
            _logger.LogInformation("Duplicate in-progress job detected. Skipping.");
            if (!string.IsNullOrWhiteSpace(message.ReceiptHandle))
            {
                await _sqs.ChangeMessageVisibilityAsync(
                    _options.InputQueueUrl,
                    message.ReceiptHandle,
                    _options.VisibilityTimeoutSeconds,
                    stoppingToken);
            }
            return;
        }

        if (!string.Equals(job.TaskType, CanonicalTaskTypes.ChatCompletion, StringComparison.OrdinalIgnoreCase))
        {
            var published = await PublishErrorAsync(
                job,
                CanonicalError.Create("unsupported_task", $"Unsupported task type: {job.TaskType}"),
                stoppingToken);
            if (!published)
            {
                return;
            }
            await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, stoppingToken);
            await DeleteMessageAsync(message, stoppingToken);
            return;
        }

        string promptText;
        try
        {
            var template = await _templateStore.GetTemplateAsync(job, stoppingToken);
            promptText = _promptBuilder.BuildPrompt(job, template);
        }
        catch (Exception ex)
        {
            var published = await PublishErrorAsync(
                job,
                CanonicalError.Create("prompt_load_failed", ex.Message),
                stoppingToken);
            if (!published)
            {
                return;
            }
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
            var published = await PublishErrorAsync(job, error, stoppingToken, ex.RawPayload);
            if (!published)
            {
                return;
            }
            await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, stoppingToken);
            await DeleteMessageAsync(message, stoppingToken);
            return;
        }
        catch (Exception ex)
        {
            var published = await PublishErrorAsync(
                job,
                CanonicalError.Create("provider_error", ex.Message),
                stoppingToken);
            if (!published)
            {
                return;
            }
            await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, stoppingToken);
            await DeleteMessageAsync(message, stoppingToken);
            return;
        }
        finally
        {
            visibilityCts.Cancel();
            await visibilityTask;
        }

        if (openAiResult is null)
        {
            _logger.LogWarning("OpenAI result missing.");
            var published = await PublishErrorAsync(
                job,
                CanonicalError.Create("provider_error", "OpenAI result missing."),
                stoppingToken);
            if (!published)
            {
                return;
            }
            await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, stoppingToken);
            await DeleteMessageAsync(message, stoppingToken);
            return;
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
        try
        {
            await _publisher.PublishAsync(resultEvent, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish result.");
            return;
        }
        await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, stoppingToken);
        await DeleteMessageAsync(message, stoppingToken);
    }

    private async Task<bool> PublishErrorAsync(
        CanonicalJobRequest job,
        CanonicalError error,
        CancellationToken stoppingToken,
        string? rawPayload = null)
    {
        if (!string.IsNullOrWhiteSpace(rawPayload))
        {
            try
            {
                error.RawPayloadReference = await _payloadStore.StoreAsync(
                    job,
                    rawPayload,
                    "error.json",
                    stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to store error payload.");
            }
        }

        var resultEvent = ResultEvent.Failure(job, error);
        resultEvent.Provider = _options.ProviderName;
        try
        {
            await _publisher.PublishAsync(resultEvent, stoppingToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish error result.");
            return false;
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
