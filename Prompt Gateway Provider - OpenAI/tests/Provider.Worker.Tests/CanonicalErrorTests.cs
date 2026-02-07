using Provider.Worker.Models;
using Provider.Worker.Services;

namespace Provider.Worker.Tests;

public class CanonicalErrorTests
{
    [Test]
    public void FromOpenAi_MapsRateLimitError()
    {
        var exception = new OpenAiException("rate_limit_error", "slow down", "{}");

        var error = CanonicalError.FromOpenAi(exception);

        Assert.That(error.Code, Is.EqualTo("rate_limited"));
        Assert.That(error.ProviderErrorType, Is.EqualTo("rate_limit_error"));
    }
}
