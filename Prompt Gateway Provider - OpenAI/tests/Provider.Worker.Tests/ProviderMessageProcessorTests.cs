using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Provider.Worker.Models;
using Provider.Worker.Options;
using Provider.Worker.Services;

namespace Provider.Worker.Tests;

public class ProviderMessageProcessorTests
{
    [Test]
    public async Task ProcessAsync_AcknowledgesSuccessfulMessage()
    {
        var options = CreateOptions();
        var dedupe = Substitute.For<IDedupeStore>();
        var templateStore = Substitute.For<IPromptTemplateStore>();
        var promptBuilder = Substitute.For<IPromptBuilder>();
        var openAi = Substitute.For<IOpenAiClient>();
        var payloadStore = Substitute.For<IResultPayloadStore>();
        var publisher = Substitute.For<IResultPublisher>();
        var processor = CreateProcessor(options, dedupe, templateStore, promptBuilder, openAi, payloadStore, publisher);

        dedupe.TryStartAsync("job-1", "attempt-1", Arg.Any<CancellationToken>())
            .Returns(DedupeDecision.Started);
        templateStore.GetTemplateAsync(Arg.Any<CanonicalJobRequest>(), Arg.Any<CancellationToken>())
            .Returns("template");
        promptBuilder.BuildPrompt(Arg.Any<CanonicalJobRequest>(), "template")
            .Returns("prompt");
        openAi.ExecuteAsync(Arg.Any<CanonicalJobRequest>(), "prompt", Arg.Any<CancellationToken>())
            .Returns(new OpenAiResult
            {
                Content = "ok",
                Model = "gpt-test",
                Usage = new UsageMetrics { TotalTokens = 1 }
            });

        var result = await processor.ProcessAsync(new QueueMessage
        {
            Body = """
                   { "job_id": "job-1", "attempt_id": "attempt-1", "task_type": "chat_completion", "prompt_s3_key": "prompts/job-1.txt" }
                   """
        }, CancellationToken.None);

        Assert.That(result.ShouldAcknowledge, Is.True);
        Assert.That(result.ShouldExtendVisibilityTimeout, Is.False);
        await publisher.Received(1).PublishAsync(Arg.Any<ResultEvent>(), Arg.Any<CancellationToken>());
        await dedupe.Received(1).MarkCompletedAsync("job-1", "attempt-1", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_RetriesDuplicateInProgress()
    {
        var options = CreateOptions();
        var dedupe = Substitute.For<IDedupeStore>();
        var templateStore = Substitute.For<IPromptTemplateStore>();
        var promptBuilder = Substitute.For<IPromptBuilder>();
        var openAi = Substitute.For<IOpenAiClient>();
        var payloadStore = Substitute.For<IResultPayloadStore>();
        var publisher = Substitute.For<IResultPublisher>();
        var processor = CreateProcessor(options, dedupe, templateStore, promptBuilder, openAi, payloadStore, publisher);

        dedupe.TryStartAsync("job-2", "attempt-2", Arg.Any<CancellationToken>())
            .Returns(DedupeDecision.DuplicateInProgress);

        var result = await processor.ProcessAsync(new QueueMessage
        {
            Body = """
                   { "job_id": "job-2", "attempt_id": "attempt-2", "task_type": "chat_completion", "prompt_s3_key": "prompts/job-2.txt" }
                   """
        }, CancellationToken.None);

        Assert.That(result.ShouldAcknowledge, Is.False);
        Assert.That(result.ShouldExtendVisibilityTimeout, Is.True);
        await publisher.DidNotReceive().PublishAsync(Arg.Any<ResultEvent>(), Arg.Any<CancellationToken>());
        await dedupe.DidNotReceive().MarkCompletedAsync("job-2", "attempt-2", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_RetriesWhenPublishingResultFails()
    {
        var options = CreateOptions();
        var dedupe = Substitute.For<IDedupeStore>();
        var templateStore = Substitute.For<IPromptTemplateStore>();
        var promptBuilder = Substitute.For<IPromptBuilder>();
        var openAi = Substitute.For<IOpenAiClient>();
        var payloadStore = Substitute.For<IResultPayloadStore>();
        var publisher = Substitute.For<IResultPublisher>();
        var processor = CreateProcessor(options, dedupe, templateStore, promptBuilder, openAi, payloadStore, publisher);

        dedupe.TryStartAsync("job-3", "attempt-3", Arg.Any<CancellationToken>())
            .Returns(DedupeDecision.Started);
        templateStore.GetTemplateAsync(Arg.Any<CanonicalJobRequest>(), Arg.Any<CancellationToken>())
            .Returns("template");
        promptBuilder.BuildPrompt(Arg.Any<CanonicalJobRequest>(), "template")
            .Returns("prompt");
        openAi.ExecuteAsync(Arg.Any<CanonicalJobRequest>(), "prompt", Arg.Any<CancellationToken>())
            .Returns(new OpenAiResult
            {
                Content = "ok",
                Model = "gpt-test",
                Usage = new UsageMetrics { TotalTokens = 1 }
            });
        publisher.PublishAsync(Arg.Any<ResultEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new Exception("publish failed")));

        var result = await processor.ProcessAsync(new QueueMessage
        {
            Body = """
                   { "job_id": "job-3", "attempt_id": "attempt-3", "task_type": "chat_completion", "prompt_s3_key": "prompts/job-3.txt" }
                   """
        }, CancellationToken.None);

        Assert.That(result.ShouldAcknowledge, Is.False);
        Assert.That(result.ShouldExtendVisibilityTimeout, Is.False);
        await dedupe.DidNotReceive().MarkCompletedAsync("job-3", "attempt-3", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_PublishesTerminalProviderErrorAndAcknowledges()
    {
        var options = CreateOptions();
        var dedupe = Substitute.For<IDedupeStore>();
        var templateStore = Substitute.For<IPromptTemplateStore>();
        var promptBuilder = Substitute.For<IPromptBuilder>();
        var openAi = Substitute.For<IOpenAiClient>();
        var payloadStore = Substitute.For<IResultPayloadStore>();
        var publisher = Substitute.For<IResultPublisher>();
        var processor = CreateProcessor(options, dedupe, templateStore, promptBuilder, openAi, payloadStore, publisher);

        dedupe.TryStartAsync("job-4", "attempt-4", Arg.Any<CancellationToken>())
            .Returns(DedupeDecision.Started);
        templateStore.GetTemplateAsync(Arg.Any<CanonicalJobRequest>(), Arg.Any<CancellationToken>())
            .Returns("template");
        promptBuilder.BuildPrompt(Arg.Any<CanonicalJobRequest>(), "template")
            .Returns("prompt");
        openAi.ExecuteAsync(Arg.Any<CanonicalJobRequest>(), "prompt", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<OpenAiResult>(new OpenAiException("authentication_error", "bad key", rawPayload: null)));

        var result = await processor.ProcessAsync(new QueueMessage
        {
            Body = """
                   { "job_id": "job-4", "attempt_id": "attempt-4", "task_type": "chat_completion", "prompt_s3_key": "prompts/job-4.txt" }
                   """
        }, CancellationToken.None);

        Assert.That(result.ShouldAcknowledge, Is.True);
        Assert.That(result.ShouldExtendVisibilityTimeout, Is.False);
        await publisher.Received(1).PublishAsync(
            Arg.Is<ResultEvent>(evt =>
                evt.Status == "failed" &&
                evt.Error != null &&
                evt.Error.Code == "auth_error" &&
                evt.Error.ProviderErrorType == "authentication_error"),
            Arg.Any<CancellationToken>());
        await dedupe.Received(1).MarkCompletedAsync("job-4", "attempt-4", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_RetriesWhenPublishingTerminalProviderErrorFails()
    {
        var options = CreateOptions();
        var dedupe = Substitute.For<IDedupeStore>();
        var templateStore = Substitute.For<IPromptTemplateStore>();
        var promptBuilder = Substitute.For<IPromptBuilder>();
        var openAi = Substitute.For<IOpenAiClient>();
        var payloadStore = Substitute.For<IResultPayloadStore>();
        var publisher = Substitute.For<IResultPublisher>();
        var processor = CreateProcessor(options, dedupe, templateStore, promptBuilder, openAi, payloadStore, publisher);

        dedupe.TryStartAsync("job-5", "attempt-5", Arg.Any<CancellationToken>())
            .Returns(DedupeDecision.Started);
        templateStore.GetTemplateAsync(Arg.Any<CanonicalJobRequest>(), Arg.Any<CancellationToken>())
            .Returns("template");
        promptBuilder.BuildPrompt(Arg.Any<CanonicalJobRequest>(), "template")
            .Returns("prompt");
        openAi.ExecuteAsync(Arg.Any<CanonicalJobRequest>(), "prompt", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<OpenAiResult>(new OpenAiException("invalid_request_error", "bad request", rawPayload: null)));
        publisher.PublishAsync(Arg.Any<ResultEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new Exception("publish failed")));

        var result = await processor.ProcessAsync(new QueueMessage
        {
            Body = """
                   { "job_id": "job-5", "attempt_id": "attempt-5", "task_type": "chat_completion", "prompt_s3_key": "prompts/job-5.txt" }
                   """
        }, CancellationToken.None);

        Assert.That(result.ShouldAcknowledge, Is.False);
        Assert.That(result.ShouldExtendVisibilityTimeout, Is.False);
        await dedupe.DidNotReceive().MarkCompletedAsync("job-5", "attempt-5", Arg.Any<CancellationToken>());
    }

    private static ProviderMessageProcessor CreateProcessor(
        IOptions<ProviderWorkerOptions> options,
        IDedupeStore dedupe,
        IPromptTemplateStore templateStore,
        IPromptBuilder promptBuilder,
        IOpenAiClient openAi,
        IResultPayloadStore payloadStore,
        IResultPublisher publisher)
    {
        return new ProviderMessageProcessor(
            Substitute.For<ILogger<ProviderMessageProcessor>>(),
            options,
            dedupe,
            templateStore,
            promptBuilder,
            openAi,
            payloadStore,
            publisher);
    }

    private static IOptions<ProviderWorkerOptions> CreateOptions()
    {
        return TestOptions.Create(new ProviderWorkerOptions
        {
            InputQueueUrl = "https://sqs.us-east-1.amazonaws.com/123/input",
            OutputQueueUrl = "https://sqs.us-east-1.amazonaws.com/123/output",
            MaxConcurrency = 1,
            MaxMessages = 1,
            WaitTimeSeconds = 0,
            VisibilityTimeoutSeconds = 30
        });
    }
}
