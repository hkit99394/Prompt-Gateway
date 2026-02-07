using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

public class OpenAiClient(HttpClient httpClient, IOptions<ProviderWorkerOptions> options) : IOpenAiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = httpClient;
    private readonly ProviderWorkerOptions _options = options.Value;

    public async Task<OpenAiResult> ExecuteAsync(
        CanonicalJobRequest job,
        string promptText,
        CancellationToken cancellationToken)
    {
        var request = BuildRequest(job, promptText);
        var json = JsonSerializer.Serialize(request, JsonOptions);

        var attempt = 0;
        var maxAttempts = 3;

        while (true)
        {
            attempt++;
            using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenAi.ApiKey);

            using var response = await _httpClient.SendAsync(message, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var parsed = JsonSerializer.Deserialize<OpenAiChatResponse>(responseBody, JsonOptions);
                if (parsed is null || parsed.Choices.Count == 0)
                {
                    throw new OpenAiException("empty_response", "OpenAI returned an empty response.", responseBody);
                }

                var choice = parsed.Choices[0];
                return new OpenAiResult
                {
                    Content = choice.Message?.Content ?? string.Empty,
                    FinishReason = choice.FinishReason,
                    Model = parsed.Model,
                    Usage = new UsageMetrics
                    {
                        PromptTokens = parsed.Usage?.PromptTokens,
                        CompletionTokens = parsed.Usage?.CompletionTokens,
                        TotalTokens = parsed.Usage?.TotalTokens
                    },
                    RawJson = responseBody
                };
            }

            if (ShouldRetry(response.StatusCode) && attempt < maxAttempts)
            {
                await Task.Delay(BackoffDelay(attempt), cancellationToken);
                continue;
            }

            var error = ParseError(responseBody);
            throw new OpenAiException(error.Type ?? "provider_error", error.Message ?? "OpenAI request failed.", responseBody);
        }
    }

    private OpenAiChatRequest BuildRequest(CanonicalJobRequest job, string promptText)
    {
        var parameters = job.Parameters ?? new OpenAiParameters();
        var temperature = Normalize(parameters.Temperature ?? _options.OpenAi.Temperature, 0, 2);
        var maxTokens = NormalizeMaxTokens(parameters.MaxTokens ?? _options.OpenAi.MaxTokens);
        var topP = NormalizeNullable(parameters.TopP, 0, 1);
        var presencePenalty = NormalizeNullable(parameters.PresencePenalty, -2, 2);
        var frequencyPenalty = NormalizeNullable(parameters.FrequencyPenalty, -2, 2);

        var messages = new List<OpenAiChatMessage>();
        if (!string.IsNullOrWhiteSpace(job.SystemPrompt))
        {
            messages.Add(new OpenAiChatMessage
            {
                Role = "system",
                Content = job.SystemPrompt
            });
        }

        messages.Add(new OpenAiChatMessage
        {
            Role = "user",
            Content = promptText
        });

        var model = string.IsNullOrWhiteSpace(job.Model) ? _options.OpenAi.Model : job.Model;

        return new OpenAiChatRequest
        {
            Model = model,
            Messages = messages,
            Temperature = temperature,
            MaxTokens = maxTokens,
            TopP = topP,
            PresencePenalty = presencePenalty,
            FrequencyPenalty = frequencyPenalty
        };
    }

    private static double Normalize(double value, double min, double max)
    {
        return Math.Clamp(value, min, max);
    }

    private static double? NormalizeNullable(double? value, double min, double max)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return Math.Clamp(value.Value, min, max);
    }

    private static int? NormalizeMaxTokens(int? value)
    {
        if (!value.HasValue || value.Value <= 0)
        {
            return null;
        }

        return value.Value;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests
            || statusCode == HttpStatusCode.RequestTimeout
            || statusCode == HttpStatusCode.BadGateway
            || statusCode == HttpStatusCode.ServiceUnavailable
            || statusCode == HttpStatusCode.GatewayTimeout
            || statusCode == HttpStatusCode.InternalServerError;
    }

    private static TimeSpan BackoffDelay(int attempt)
    {
        var jitter = Random.Shared.NextDouble() * 0.3 + 0.85;
        var seconds = Math.Pow(2, attempt) * jitter;
        return TimeSpan.FromSeconds(Math.Min(10, seconds));
    }

    private static OpenAiErrorPayload ParseError(string responseBody)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<OpenAiErrorWrapper>(responseBody, JsonOptions);
            return parsed?.Error ?? new OpenAiErrorPayload();
        }
        catch
        {
            return new OpenAiErrorPayload { Message = responseBody };
        }
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

internal class OpenAiChatRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("messages")]
    public List<OpenAiChatMessage> Messages { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("top_p")]
    public double? TopP { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("presence_penalty")]
    public double? PresencePenalty { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("frequency_penalty")]
    public double? FrequencyPenalty { get; set; }
}

internal class OpenAiChatMessage
{
    [System.Text.Json.Serialization.JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

internal class OpenAiChatResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("model")]
    public string? Model { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("choices")]
    public List<OpenAiChatChoice> Choices { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; set; }
}

internal class OpenAiChatChoice
{
    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public OpenAiChatMessage? Message { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal class OpenAiUsage
{
    [System.Text.Json.Serialization.JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("total_tokens")]
    public int? TotalTokens { get; set; }
}

internal class OpenAiErrorWrapper
{
    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public OpenAiErrorPayload? Error { get; set; }
}

internal class OpenAiErrorPayload
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string? Type { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string? Message { get; set; }
}
