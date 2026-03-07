using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlPlane.Core;

public static class ProviderResultEventContractMapper
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParseWorkerResultEvent(
        string json,
        out ProviderResultEvent? resultEvent,
        out string? error)
    {
        resultEvent = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Result event payload is empty.";
            return false;
        }

        WorkerResultEventContract? contract;
        try
        {
            contract = JsonSerializer.Deserialize<WorkerResultEventContract>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            error = $"Invalid result event JSON: {ex.Message}";
            return false;
        }

        if (contract is null)
        {
            error = "Result event payload could not be parsed.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(contract.JobId))
        {
            error = "Result event is missing job_id.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(contract.AttemptId))
        {
            error = "Result event is missing attempt_id.";
            return false;
        }

        var isSuccess = string.Equals(contract.Status, "succeeded", StringComparison.OrdinalIgnoreCase);
        if (!isSuccess && !string.Equals(contract.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Unsupported result status '{contract.Status}'.";
            return false;
        }

        if (!isSuccess && contract.Error is null)
        {
            error = "Failed result event is missing error.";
            return false;
        }

        var usage = contract.Usage ?? contract.Response?.Usage;
        var mappedUsage = usage is null
            ? null
            : new UsageMetrics(
                usage.PromptTokens ?? usage.InputTokens ?? 0,
                usage.CompletionTokens ?? usage.OutputTokens ?? 0,
                usage.TotalTokens ?? 0);

        var mappedCost = usage?.EstimatedCostUsd is decimal estimatedCost
            ? new CostMetrics(estimatedCost, "USD", true)
            : null;

        var model = contract.Response?.Model ?? string.Empty;
        var provider = contract.Provider ?? string.Empty;
        var mappedError = contract.Error is null
            ? null
            : new CanonicalError(
                string.IsNullOrWhiteSpace(contract.Error.Code) ? "provider_error" : contract.Error.Code,
                string.IsNullOrWhiteSpace(contract.Error.Message) ? "Provider call failed." : contract.Error.Message)
            {
                ProviderCode = contract.Error.ProviderCode
            };

        resultEvent = new ProviderResultEvent
        {
            JobId = contract.JobId,
            AttemptId = contract.AttemptId,
            Provider = provider,
            Model = model,
            IsSuccess = isSuccess,
            OutputRef = contract.Response?.RawPayloadReference,
            Usage = mappedUsage,
            Cost = mappedCost,
            Error = mappedError
        };

        return true;
    }

    private sealed class WorkerResultEventContract
    {
        [JsonPropertyName("job_id")]
        public string JobId { get; init; } = string.Empty;

        [JsonPropertyName("attempt_id")]
        public string AttemptId { get; init; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; init; } = string.Empty;

        [JsonPropertyName("response")]
        public WorkerCanonicalResponse? Response { get; init; }

        [JsonPropertyName("error")]
        public WorkerCanonicalError? Error { get; init; }

        [JsonPropertyName("usage")]
        public WorkerUsageMetrics? Usage { get; init; }

        [JsonPropertyName("provider")]
        public string? Provider { get; init; }
    }

    private sealed class WorkerCanonicalResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("raw_payload_ref")]
        public string? RawPayloadReference { get; init; }

        [JsonPropertyName("usage")]
        public WorkerUsageMetrics? Usage { get; init; }
    }

    private sealed class WorkerCanonicalError
    {
        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("provider_code")]
        public string? ProviderCode { get; init; }
    }

    private sealed class WorkerUsageMetrics
    {
        [JsonPropertyName("input_tokens")]
        public int? InputTokens { get; init; }

        [JsonPropertyName("output_tokens")]
        public int? OutputTokens { get; init; }

        [JsonPropertyName("prompt_tokens")]
        public int? PromptTokens { get; init; }

        [JsonPropertyName("completion_tokens")]
        public int? CompletionTokens { get; init; }

        [JsonPropertyName("total_tokens")]
        public int? TotalTokens { get; init; }

        [JsonPropertyName("estimated_cost_usd")]
        public decimal? EstimatedCostUsd { get; init; }
    }
}
