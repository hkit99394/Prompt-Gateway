using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ControlPlane.Core.Tests;

public class ResultMessageProcessorTests
{
    [Test]
    public async Task ProcessAsync_AcknowledgesPoisonMessage()
    {
        var orchestrator = Substitute.For<IResultIngestionOrchestrator>();
        var logger = Substitute.For<ILogger<ResultMessageProcessor>>();
        var processor = new ResultMessageProcessor(orchestrator, logger);

        var result = await processor.ProcessAsync("""{ "status": "succeeded" }""", "msg-1", CancellationToken.None);

        Assert.That(result.ShouldAcknowledge, Is.True);
        await orchestrator.DidNotReceive().IngestResultAsync(Arg.Any<ProviderResultEvent>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_AcknowledgesValidMessage()
    {
        var orchestrator = Substitute.For<IResultIngestionOrchestrator>();
        var logger = Substitute.For<ILogger<ResultMessageProcessor>>();
        var processor = new ResultMessageProcessor(orchestrator, logger);

        orchestrator.IngestResultAsync(Arg.Any<ProviderResultEvent>(), Arg.Any<CancellationToken>())
            .Returns(ResultIngestionOutcome.JobNotFound());

        var result = await processor.ProcessAsync(
            """
            {
              "job_id": "job-1",
              "attempt_id": "attempt-1",
              "status": "succeeded",
              "provider": "openai",
              "response": {
                "model": "gpt-4.1"
              }
            }
            """,
            "msg-2",
            CancellationToken.None);

        Assert.That(result.ShouldAcknowledge, Is.True);
        await orchestrator.Received(1).IngestResultAsync(
            Arg.Is<ProviderResultEvent>(evt => evt.JobId == "job-1" && evt.AttemptId == "attempt-1"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_RetriesWhenOrchestratorThrows()
    {
        var orchestrator = Substitute.For<IResultIngestionOrchestrator>();
        var logger = Substitute.For<ILogger<ResultMessageProcessor>>();
        var processor = new ResultMessageProcessor(orchestrator, logger);

        orchestrator.IngestResultAsync(Arg.Any<ProviderResultEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<ResultIngestionOutcome>(new InvalidOperationException("boom")));

        var result = await processor.ProcessAsync(
            """
            {
              "job_id": "job-3",
              "attempt_id": "attempt-3",
              "status": "failed",
              "provider": "openai",
              "error": {
                "code": "provider_error",
                "message": "boom"
              }
            }
            """,
            "msg-3",
            CancellationToken.None);

        Assert.That(result.ShouldAcknowledge, Is.False);
    }
}
