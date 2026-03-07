using Provider.Worker.Options;

namespace Provider.Worker.Tests;

public class ProviderWorkerOptionsTests
{
    [Test]
    public void TryValidate_ReturnsFalse_WhenOpenAiRetryMaxAttemptsIsInvalid()
    {
        var options = CreateValidOptions();
        options.OpenAiRetryMaxAttempts = 0;

        var isValid = options.TryValidate(out var error);

        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("OpenAiRetryMaxAttempts"));
    }

    [Test]
    public void TryValidate_ReturnsFalse_WhenOpenAiRetryMaxBackoffSecondsIsInvalid()
    {
        var options = CreateValidOptions();
        options.OpenAiRetryMaxBackoffSeconds = 0;

        var isValid = options.TryValidate(out var error);

        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("OpenAiRetryMaxBackoffSeconds"));
    }

    private static ProviderWorkerOptions CreateValidOptions()
    {
        return new ProviderWorkerOptions
        {
            InputQueueUrl = "in",
            OutputQueueUrl = "out",
            OpenAi = new OpenAiOptions
            {
                ApiKey = "key",
                Model = "gpt-4o-mini",
                TimeoutSeconds = 90,
                Temperature = 0.7
            }
        };
    }
}
