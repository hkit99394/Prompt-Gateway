using Provider.Worker.Options;

namespace Provider.Worker.Tests;

public class OpenAiClientTests
{
    [Test]
    public void ProviderWorkerOptions_Defaults_UseExpectedOpenAiRetrySettings()
    {
        var options = new ProviderWorkerOptions();

        Assert.That(options.OpenAiRetryMaxAttempts, Is.EqualTo(3));
        Assert.That(options.OpenAiRetryMaxBackoffSeconds, Is.EqualTo(10));
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
}
