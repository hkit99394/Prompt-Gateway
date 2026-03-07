namespace ControlPlane.Core.Tests;

public class ProviderResultEventContractMapperTests
{
    [Test]
    public void TryParseWorkerResultEvent_MapsSucceededPayload()
    {
        var json = """
                   {
                     "job_id": "job-1",
                     "attempt_id": "attempt-1",
                     "status": "succeeded",
                     "provider": "openai",
                     "response": {
                       "model": "gpt-4.1",
                       "raw_payload_ref": "s3://bucket/results/job-1/attempt-1/payload.json"
                     },
                     "usage": {
                       "prompt_tokens": 12,
                       "completion_tokens": 34,
                       "total_tokens": 46,
                       "estimated_cost_usd": 0.0123
                     }
                   }
                   """;

        var parsed = ProviderResultEventContractMapper.TryParseWorkerResultEvent(json, out var resultEvent, out var error);

        Assert.That(parsed, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(resultEvent, Is.Not.Null);
        Assert.That(resultEvent!.JobId, Is.EqualTo("job-1"));
        Assert.That(resultEvent.AttemptId, Is.EqualTo("attempt-1"));
        Assert.That(resultEvent.Provider, Is.EqualTo("openai"));
        Assert.That(resultEvent.Model, Is.EqualTo("gpt-4.1"));
        Assert.That(resultEvent.IsSuccess, Is.True);
        Assert.That(resultEvent.OutputRef, Does.StartWith("s3://"));
        Assert.That(resultEvent.Usage, Is.EqualTo(new UsageMetrics(12, 34, 46)));
        Assert.That(resultEvent.Cost, Is.EqualTo(new CostMetrics(0.0123m, "USD", true)));
        Assert.That(resultEvent.Error, Is.Null);
    }

    [Test]
    public void TryParseWorkerResultEvent_MapsFailedPayload()
    {
        var json = """
                   {
                     "job_id": "job-2",
                     "attempt_id": "attempt-2",
                     "status": "failed",
                     "provider": "openai",
                     "response": {
                       "model": "gpt-4.1"
                     },
                     "error": {
                       "code": "rate_limited",
                       "message": "Rate limit exceeded",
                       "provider_code": "429"
                     }
                   }
                   """;

        var parsed = ProviderResultEventContractMapper.TryParseWorkerResultEvent(json, out var resultEvent, out var error);

        Assert.That(parsed, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(resultEvent, Is.Not.Null);
        Assert.That(resultEvent!.IsSuccess, Is.False);
        Assert.That(resultEvent.Error, Is.Not.Null);
        Assert.That(resultEvent.Error!.Code, Is.EqualTo("rate_limited"));
        Assert.That(resultEvent.Error.ProviderCode, Is.EqualTo("429"));
    }

    [Test]
    public void TryParseWorkerResultEvent_RejectsMissingIdentifiers()
    {
        var json = """
                   {
                     "status": "succeeded",
                     "provider": "openai"
                   }
                   """;

        var parsed = ProviderResultEventContractMapper.TryParseWorkerResultEvent(json, out _, out var error);

        Assert.That(parsed, Is.False);
        Assert.That(error, Does.Contain("job_id"));
    }
}
