using System.Text.Json.Serialization;
using Provider.Worker.Services;

namespace Provider.Worker.Models;

public class CanonicalError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "provider_error";

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("provider_error_type")]
    public string? ProviderErrorType { get; set; }

    [JsonPropertyName("raw_payload_ref")]
    public string? RawPayloadReference { get; set; }

    public static CanonicalError Create(string code, string message)
    {
        return new CanonicalError
        {
            Code = code,
            Message = message
        };
    }

    public static CanonicalError FromOpenAi(OpenAiException exception)
    {
        var code = exception.ErrorType switch
        {
            "rate_limit_error" => "rate_limited",
            "invalid_request_error" => "invalid_request",
            "authentication_error" => "auth_error",
            "permission_error" => "permission_denied",
            "server_error" => "provider_error",
            _ => "provider_error"
        };

        return new CanonicalError
        {
            Code = code,
            Message = exception.Message,
            ProviderErrorType = exception.ErrorType
        };
    }
}
