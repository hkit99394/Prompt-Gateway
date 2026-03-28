using Provider.Worker.Options;
using Provider.Worker.Services;

namespace Provider.Worker.Tests;

public class OpenAiClientTests
{
    [Test]
    public void ProviderWorkerOptions_Defaults_UseExpectedOpenAiRetrySettings()
    {
        var options = new ProviderWorkerOptions();

        Assert.That(options.OpenAiRetryMaxAttempts, Is.EqualTo(3));
        Assert.That(options.OpenAiRetryMaxBackoffSeconds, Is.EqualTo(10));
        Assert.That(options.ProcessingOverheadBufferSeconds, Is.EqualTo(15));
        Assert.That(options.ExecutionTimeoutSeconds, Is.EqualTo(0));
    }

    [Test]
    public void TryValidate_Fails_WhenOpenAiRetryMaxAttemptsIsZero()
    {
        var options = new ProviderWorkerOptions
        {
            InputQueueUrl = "in",
            OutputQueueUrl = "out",
            OpenAiRetryMaxAttempts = 0,
            OpenAi = new OpenAiOptions
            {
                ApiKey = "key",
                Model = "gpt-4o-mini"
            }
        };

        var isValid = options.TryValidate(out var error);

        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("OpenAiRetryMaxAttempts"));
    }

    [Test]
    public void TryValidate_Fails_WhenOpenAiRetryMaxBackoffSecondsIsZero()
    {
        var options = new ProviderWorkerOptions
        {
            InputQueueUrl = "in",
            OutputQueueUrl = "out",
            OpenAiRetryMaxBackoffSeconds = 0,
            OpenAi = new OpenAiOptions
            {
                ApiKey = "key",
                Model = "gpt-4o-mini"
            }
        };

        var isValid = options.TryValidate(out var error);

        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("OpenAiRetryMaxBackoffSeconds"));
    }

    [TestCase(0)]
    [TestCase(408)]
    [TestCase(429)]
    [TestCase(500)]
    [TestCase(502)]
    [TestCase(503)]
    [TestCase(504)]
    public void IsRetryableStatus_ReturnsTrue_ForTransientStatuses(int statusCode)
    {
        Assert.That(OpenAiFailureClassifier.IsRetryableStatus(statusCode), Is.True);
    }

    [TestCase(400)]
    [TestCase(401)]
    [TestCase(403)]
    [TestCase(404)]
    [TestCase(422)]
    public void IsRetryableStatus_ReturnsFalse_ForTerminalStatuses(int statusCode)
    {
        Assert.That(OpenAiFailureClassifier.IsRetryableStatus(statusCode), Is.False);
    }

    [TestCase("rate_limit_error")]
    [TestCase("server_error")]
    [TestCase("api_connection_error")]
    [TestCase("service_unavailable_error")]
    [TestCase("timeout_error")]
    public void IsRetryableErrorType_ReturnsTrue_ForTransientProviderErrors(string errorType)
    {
        Assert.That(OpenAiFailureClassifier.IsRetryableErrorType(errorType), Is.True);
    }

    [TestCase("invalid_request_error")]
    [TestCase("authentication_error")]
    [TestCase("permission_error")]
    [TestCase("provider_error")]
    [TestCase(null)]
    public void IsRetryableErrorType_ReturnsFalse_ForTerminalProviderErrors(string? errorType)
    {
        Assert.That(OpenAiFailureClassifier.IsRetryableErrorType(errorType), Is.False);
    }

    [Test]
    public void Translate_ReturnsRetryableOpenAiException_ForHttpRequestException()
    {
        var exception = OpenAiFailureClassifier.Translate(new HttpRequestException("network issue"));

        Assert.That(exception.ErrorType, Is.EqualTo("api_connection_error"));
        Assert.That(exception.IsRetryable, Is.True);
    }

    [Test]
    public void Translate_ReturnsTerminalOpenAiException_ForUnexpectedException()
    {
        var exception = OpenAiFailureClassifier.Translate(new InvalidOperationException("boom"));

        Assert.That(exception.ErrorType, Is.EqualTo("provider_error"));
        Assert.That(exception.IsRetryable, Is.False);
    }
}
