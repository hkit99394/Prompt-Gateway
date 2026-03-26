using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Provider.Worker.Models;
using Provider.Worker.Options;

namespace Provider.Worker.Services;

public interface IProviderMessageProcessor
{
    Task<ProviderMessageProcessResult> ProcessAsync(QueueMessage message, CancellationToken cancellationToken);
}

public sealed class ProviderMessageProcessResult
{
    private ProviderMessageProcessResult(bool shouldAcknowledge, bool shouldExtendVisibilityTimeout)
    {
        ShouldAcknowledge = shouldAcknowledge;
        ShouldExtendVisibilityTimeout = shouldExtendVisibilityTimeout;
    }

    public bool ShouldAcknowledge { get; }

    public bool ShouldExtendVisibilityTimeout { get; }

    public static ProviderMessageProcessResult Acknowledge() => new(true, false);

    public static ProviderMessageProcessResult Retry(bool shouldExtendVisibilityTimeout = false) =>
        new(false, shouldExtendVisibilityTimeout);
}

public sealed class ProviderMessageProcessor : IProviderMessageProcessor
{
    private readonly ILogger<ProviderMessageProcessor> _logger;
    private readonly ProviderWorkerOptions _options;
    private readonly IDedupeStore _dedupeStore;
    private readonly IPromptTemplateStore _templateStore;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IOpenAiClient _openAiClient;
    private readonly IResultPayloadStore _payloadStore;
    private readonly IResultPublisher _publisher;
    private readonly JsonSerializerOptions _jsonOptions;

    public ProviderMessageProcessor(
        ILogger<ProviderMessageProcessor> logger,
        IOptions<ProviderWorkerOptions> options,
        IDedupeStore dedupeStore,
        IPromptTemplateStore templateStore,
        IPromptBuilder promptBuilder,
        IOpenAiClient openAiClient,
        IResultPayloadStore payloadStore,
        IResultPublisher publisher)
    {
        _logger = logger;
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

    public async Task<ProviderMessageProcessResult> ProcessAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.Body))
        {
            _logger.LogWarning("Empty message body. Acknowledging message.");
            return ProviderMessageProcessResult.Acknowledge();
        }

        CanonicalJobRequest? job;
        try
        {
            job = JsonSerializer.Deserialize<CanonicalJobRequest>(message.Body, _jsonOptions);
            if (job is null || string.IsNullOrWhiteSpace(job.JobId) || string.IsNullOrWhiteSpace(job.AttemptId))
            {
                job = TryParseDispatchMessageFormat(message.Body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize message.");
            return ProviderMessageProcessResult.Acknowledge();
        }

        if (job is null || string.IsNullOrWhiteSpace(job.JobId) || string.IsNullOrWhiteSpace(job.AttemptId))
        {
            _logger.LogWarning("Invalid job payload. Acknowledging message. Body (truncated): {Body}",
                message.Body.Length > 500 ? message.Body[..500] + "..." : message.Body);
            return ProviderMessageProcessResult.Acknowledge();
        }

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["job_id"] = job.JobId,
            ["attempt_id"] = job.AttemptId,
            ["provider"] = _options.ProviderName,
            ["model"] = string.IsNullOrWhiteSpace(job.Model) ? _options.OpenAi.Model : job.Model
        });

        var dedupeDecision = await _dedupeStore.TryStartAsync(job.JobId, job.AttemptId, cancellationToken);
        if (dedupeDecision == DedupeDecision.DuplicateCompleted)
        {
            _logger.LogInformation("Duplicate completed job detected. Acknowledging message.");
            return ProviderMessageProcessResult.Acknowledge();
        }

        if (dedupeDecision == DedupeDecision.DuplicateInProgress)
        {
            _logger.LogInformation("Duplicate in-progress job detected. Retrying later.");
            return ProviderMessageProcessResult.Retry(shouldExtendVisibilityTimeout: true);
        }

        if (!string.Equals(job.TaskType, CanonicalTaskTypes.ChatCompletion, StringComparison.OrdinalIgnoreCase))
        {
            var published = await PublishErrorAsync(
                job,
                CanonicalError.Create("unsupported_task", $"Unsupported task type: {job.TaskType}"),
                cancellationToken);
            if (!published)
            {
                return ProviderMessageProcessResult.Retry();
            }

            await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, cancellationToken);
            return ProviderMessageProcessResult.Acknowledge();
        }

        string promptText;
        try
        {
            var template = await _templateStore.GetTemplateAsync(job, cancellationToken);
            promptText = _promptBuilder.BuildPrompt(job, template);
        }
        catch (Exception ex)
        {
            var published = await PublishErrorAsync(
                job,
                CanonicalError.Create("prompt_load_failed", ex.Message),
                cancellationToken);
            if (!published)
            {
                return ProviderMessageProcessResult.Retry();
            }

            await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, cancellationToken);
            return ProviderMessageProcessResult.Acknowledge();
        }

        OpenAiResult? openAiResult;
        try
        {
            openAiResult = await _openAiClient.ExecuteAsync(job, promptText, cancellationToken);
        }
        catch (OpenAiException ex)
        {
            var error = CanonicalError.FromOpenAi(ex);
            var published = await PublishErrorAsync(job, error, cancellationToken, ex.RawPayload);
            if (!published)
            {
                return ProviderMessageProcessResult.Retry();
            }

            await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, cancellationToken);
            return ProviderMessageProcessResult.Acknowledge();
        }
        catch (Exception ex)
        {
            var published = await PublishErrorAsync(
                job,
                CanonicalError.Create("provider_error", ex.Message),
                cancellationToken);
            if (!published)
            {
                return ProviderMessageProcessResult.Retry();
            }

            await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, cancellationToken);
            return ProviderMessageProcessResult.Acknowledge();
        }

        if (openAiResult is null)
        {
            _logger.LogWarning("OpenAI result missing.");
            var published = await PublishErrorAsync(
                job,
                CanonicalError.Create("provider_error", "OpenAI result missing."),
                cancellationToken);
            if (!published)
            {
                return ProviderMessageProcessResult.Retry();
            }

            await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, cancellationToken);
            return ProviderMessageProcessResult.Acknowledge();
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
                cancellationToken);
        }

        var resultEvent = ResultEvent.Success(job, responsePayload);
        resultEvent.Provider = _options.ProviderName;

        try
        {
            await _publisher.PublishAsync(resultEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish result.");
            return ProviderMessageProcessResult.Retry();
        }

        await _dedupeStore.MarkCompletedAsync(job.JobId, job.AttemptId, cancellationToken);
        return ProviderMessageProcessResult.Acknowledge();
    }

    private async Task<bool> PublishErrorAsync(
        CanonicalJobRequest job,
        CanonicalError error,
        CancellationToken cancellationToken,
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
                    cancellationToken);
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
            await _publisher.PublishAsync(resultEvent, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish error result.");
            return false;
        }
    }

    private static CanonicalJobRequest? TryParseDispatchMessageFormat(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("jobId", out var jobIdEl) || !root.TryGetProperty("attemptId", out var attemptIdEl))
            {
                return null;
            }

            var jobId = jobIdEl.GetString();
            var attemptId = attemptIdEl.GetString();
            if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(attemptId))
            {
                return null;
            }

            var taskType = string.Empty;
            string? model = null;
            string? inputRef = null;
            string? promptKey = null;
            string? promptBucket = null;
            string? promptS3Key = null;
            string? promptS3Bucket = null;
            Dictionary<string, string>? metadata = null;

            if (root.TryGetProperty("request", out var requestEl))
            {
                if (requestEl.TryGetProperty("taskType", out var tt))
                {
                    taskType = tt.GetString() ?? string.Empty;
                }

                if (requestEl.TryGetProperty("inputRef", out var inputRefEl))
                {
                    inputRef = inputRefEl.GetString();
                }

                if (requestEl.TryGetProperty("promptKey", out var promptKeyEl))
                {
                    promptKey = promptKeyEl.GetString();
                }

                if (requestEl.TryGetProperty("promptBucket", out var promptBucketEl))
                {
                    promptBucket = promptBucketEl.GetString();
                }

                if (requestEl.TryGetProperty("promptS3Key", out var promptS3KeyEl))
                {
                    promptS3Key = promptS3KeyEl.GetString();
                }

                if (requestEl.TryGetProperty("promptS3Bucket", out var promptS3BucketEl))
                {
                    promptS3Bucket = promptS3BucketEl.GetString();
                }

                if (requestEl.TryGetProperty("model", out var m))
                {
                    model = m.GetString();
                }

                if (requestEl.TryGetProperty("metadata", out var metaEl) && metaEl.ValueKind == JsonValueKind.Object)
                {
                    metadata = new Dictionary<string, string>();
                    foreach (var property in metaEl.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            metadata[property.Name] = property.Value.GetString() ?? string.Empty;
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(model) && root.TryGetProperty("model", out var modelEl))
            {
                model = modelEl.GetString();
            }

            return new CanonicalJobRequest
            {
                JobId = jobId,
                AttemptId = attemptId,
                TaskType = string.IsNullOrWhiteSpace(taskType) ? CanonicalTaskTypes.ChatCompletion : taskType,
                InputRef = inputRef,
                PromptKey = promptKey,
                PromptBucket = promptBucket,
                PromptS3Key = promptS3Key,
                PromptS3Bucket = promptS3Bucket,
                Model = model,
                Metadata = metadata
            };
        }
        catch
        {
            return null;
        }
    }
}
