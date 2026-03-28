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

    [Test]
    public void TryValidate_ReturnsFalse_WhenExecutionTimeoutIsSmallerThanWorstCaseInvocationWindow()
    {
        var options = CreateValidOptions();
        options.MaxMessages = 1;
        options.ExecutionTimeoutSeconds = 200;

        var isValid = options.TryValidate(out var error);

        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("ExecutionTimeoutSeconds"));
        Assert.That(error, Does.Contain("worst-case invocation window"));
    }

    [Test]
    public void TryValidate_ReturnsFalse_WhenVisibilityTimeoutIsSmallerThanWorstCaseInvocationWindow()
    {
        var options = CreateValidOptions();
        options.MaxMessages = 1;
        options.ExecutionTimeoutSeconds = 300;
        options.VisibilityTimeoutSeconds = 250;

        var isValid = options.TryValidate(out var error);

        Assert.That(isValid, Is.False);
        Assert.That(error, Does.Contain("VisibilityTimeoutSeconds"));
        Assert.That(error, Does.Contain("worst-case invocation window"));
    }

    [Test]
    public void TryValidate_ReturnsTrue_WhenLambdaBudgetIsLargeEnough()
    {
        var options = CreateValidOptions();
        options.MaxMessages = 1;
        options.ExecutionTimeoutSeconds = 300;
        options.VisibilityTimeoutSeconds = 300;

        var isValid = options.TryValidate(out var error);

        Assert.That(isValid, Is.True, error);
    }

    [Test]
    public void GetWorstCaseInvocationProcessingWindowSeconds_UsesRetryBudgetAndBatchSize()
    {
        var options = CreateValidOptions();
        options.MaxMessages = 2;
        options.OpenAiRetryMaxAttempts = 3;
        options.OpenAiRetryMaxBackoffSeconds = 10;
        options.OpenAi.TimeoutSeconds = 90;
        options.ProcessingOverheadBufferSeconds = 15;

        var window = options.GetWorstCaseInvocationProcessingWindowSeconds();

        Assert.That(window, Is.EqualTo(586));
    }

    private static ProviderWorkerOptions CreateValidOptions()
    {
        return new ProviderWorkerOptions
        {
            InputQueueUrl = "in",
            OutputQueueUrl = "out",
            VisibilityTimeoutSeconds = 300,
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
