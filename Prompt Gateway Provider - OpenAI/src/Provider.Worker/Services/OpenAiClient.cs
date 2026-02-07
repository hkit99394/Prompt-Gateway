using System.Text.Json;
using OpenAI;
using System.ClientModel;
using OpenAI.Responses;
using Microsoft.Extensions.Options;
using Provider.Worker.Models;
using Provider.Worker.Options;

namespace Provider.Worker.Services;

public interface IOpenAiClient
{
    Task<OpenAiResult> ExecuteAsync(
        CanonicalJobRequest job,
        string promptText,
        CancellationToken cancellationToken);
}

public class OpenAiClient(IOptions<ProviderWorkerOptions> options) : IOpenAiClient
{
    private readonly ProviderWorkerOptions _options = options.Value;
    private readonly OpenAIClient _client = CreateClient(options.Value);

    private static OpenAIClient CreateClient(ProviderWorkerOptions options)
    {
        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(options.OpenAi.BaseUrl))
        {
            clientOptions.Endpoint = new Uri(options.OpenAi.BaseUrl);
        }

        return new OpenAIClient(new ApiKeyCredential(options.OpenAi.ApiKey), clientOptions);
    }

    public async Task<OpenAiResult> ExecuteAsync(
        CanonicalJobRequest job,
        string promptText,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        var maxAttempts = 3;

        while (true)
        {
            attempt++;
            try
            {
                return await CreateResponseAsync(job, promptText, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                await Task.Delay(BackoffDelay(attempt), cancellationToken);
            }
        }
    }

    private async Task<OpenAiResult> CreateResponseAsync(
        CanonicalJobRequest job,
        string promptText,
        CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(job.Model) ? _options.OpenAi.Model : job.Model;

        var responsesClient = _client.GetResponsesClient(model);
        var items = new List<ResponseItem>();
        if (!string.IsNullOrWhiteSpace(job.SystemPrompt))
        {
            items.Add(ResponseItem.CreateSystemMessageItem(job.SystemPrompt));
        }
        items.Add(ResponseItem.CreateUserMessageItem(promptText));

        var result = await responsesClient.CreateResponseAsync(
            items,
            previousResponseId: null,
            cancellationToken: cancellationToken);
        var response = result.Value;
        if (response.Error is not null ||
            response.Status is null ||
            response.Status != ResponseStatus.Completed)
        {
            var errorMessage = response.Error?.Message
                               ?? $"OpenAI response status: {response.Status}";
            throw new OpenAiException(
                response.Error?.Code.ToString() ?? "provider_error",
                errorMessage,
                JsonSerializer.Serialize(response));
        }
        var rawJson = JsonSerializer.Serialize(response);
        var outputText = response.GetOutputText() ?? string.Empty;

        return new OpenAiResult
        {
            Content = outputText,
            FinishReason = response.Status?.ToString(),
            Model = response.Model,
            Usage = MapUsage(response.Usage),
            RawJson = rawJson
        };
    }

    private static TimeSpan BackoffDelay(int attempt)
    {
        var jitter = Random.Shared.NextDouble() * 0.3 + 0.85;
        var seconds = Math.Pow(2, attempt) * jitter;
        return TimeSpan.FromSeconds(Math.Min(10, seconds));
    }

    private static UsageMetrics? MapUsage(ResponseTokenUsage? usage)
    {
        if (usage is null)
        {
            return null;
        }

        return new UsageMetrics
        {
            InputTokens = usage.InputTokenCount,
            InputTokensDetails = usage.InputTokenDetails is null
                ? null
                : new InputTokensDetails
                {
                    CachedTokens = usage.InputTokenDetails.CachedTokenCount
                },
            OutputTokens = usage.OutputTokenCount,
            OutputTokensDetails = usage.OutputTokenDetails is null
                ? null
                : new OutputTokensDetails
                {
                    ReasoningTokens = usage.OutputTokenDetails.ReasoningTokenCount
                },
            PromptTokens = usage.InputTokenCount,
            CompletionTokens = usage.OutputTokenCount,
            TotalTokens = usage.TotalTokenCount
        };
    }
}

public class OpenAiException(string errorType, string message, string? rawPayload) : Exception(message)
{
    public string ErrorType { get; } = errorType;

    public string? RawPayload { get; } = rawPayload;
}

public class OpenAiResult
{
    public string Content { get; set; } = string.Empty;
    public string? FinishReason { get; set; }
    public string? Model { get; set; }
    public UsageMetrics? Usage { get; set; }
    public string? RawJson { get; set; }
}

