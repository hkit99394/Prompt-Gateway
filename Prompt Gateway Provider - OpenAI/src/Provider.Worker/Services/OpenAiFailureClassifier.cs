using System.ClientModel;
using System.IO;

namespace Provider.Worker.Services;

public static class OpenAiFailureClassifier
{
    public static bool ShouldRetry(Exception exception)
    {
        return exception switch
        {
            OpenAiException openAiException => openAiException.IsRetryable,
            ClientResultException clientResultException => IsRetryableStatus(clientResultException.Status),
            HttpRequestException => true,
            TimeoutException => true,
            IOException => true,
            OperationCanceledException => true,
            _ => false
        };
    }

    public static bool IsRetryableStatus(int statusCode)
    {
        return statusCode is 0 or 408 or 429 or 500 or 502 or 503 or 504;
    }

    public static bool IsRetryableErrorType(string? errorType)
    {
        return errorType switch
        {
            "rate_limit_error" => true,
            "server_error" => true,
            "api_connection_error" => true,
            "service_unavailable_error" => true,
            "timeout_error" => true,
            _ => false
        };
    }

    public static OpenAiException Translate(Exception exception)
    {
        return exception switch
        {
            OpenAiException openAiException => openAiException,
            ClientResultException clientResultException => new OpenAiException(
                MapStatusToErrorType(clientResultException.Status),
                clientResultException.Message,
                rawPayload: null,
                isRetryable: IsRetryableStatus(clientResultException.Status)),
            HttpRequestException httpRequestException => new OpenAiException(
                "api_connection_error",
                httpRequestException.Message,
                rawPayload: null,
                isRetryable: true),
            TimeoutException timeoutException => new OpenAiException(
                "timeout_error",
                timeoutException.Message,
                rawPayload: null,
                isRetryable: true),
            OperationCanceledException operationCanceledException => new OpenAiException(
                "timeout_error",
                operationCanceledException.Message,
                rawPayload: null,
                isRetryable: true),
            IOException ioException => new OpenAiException(
                "api_connection_error",
                ioException.Message,
                rawPayload: null,
                isRetryable: true),
            _ => new OpenAiException(
                "provider_error",
                exception.Message,
                rawPayload: null,
                isRetryable: false)
        };
    }

    private static string MapStatusToErrorType(int statusCode)
    {
        return statusCode switch
        {
            400 or 404 or 422 => "invalid_request_error",
            401 => "authentication_error",
            403 => "permission_error",
            408 => "timeout_error",
            429 => "rate_limit_error",
            500 or 502 or 503 or 504 => "server_error",
            _ => "provider_error"
        };
    }
}
