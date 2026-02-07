using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Provider.Worker.Models;
using Provider.Worker.Options;
using Provider.Worker.Services;

namespace Provider.Worker.Tests;

public class WorkerTests
{
    [Test]
    public async Task RunAsync_ProcessesMessageAndDeletesIt()
    {
        var logger = Substitute.For<ILogger<Worker>>();
        var sqs = Substitute.For<IQueueClient>();
        var options = CreateOptions();
        var dedupe = Substitute.For<IDedupeStore>();
        var templateStore = Substitute.For<IPromptTemplateStore>();
        var promptBuilder = Substitute.For<IPromptBuilder>();
        var openAi = Substitute.For<IOpenAiClient>();
        var payloadStore = Substitute.For<IResultPayloadStore>();
        var publisher = Substitute.For<IResultPublisher>();

        var message = new QueueMessage
        {
            Body = """
                   { "job_id": "job-1", "attempt_id": "attempt-1", "task_type": "chat_completion", "prompt_s3_key": "prompts/job-1.txt" }
                   """,
            ReceiptHandle = "rh-1"
        };

        sqs.ReceiveMessageAsync(Arg.Any<QueueReceiveRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new QueueReceiveResult { Messages = new List<QueueMessage> { message } },
                new QueueReceiveResult());

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

        var published = new TaskCompletionSource();
        publisher.PublishAsync(Arg.Any<ResultEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                published.SetResult();
                return Task.CompletedTask;
            });

        var worker = new Worker(
            logger,
            sqs,
            options,
            dedupe,
            templateStore,
            promptBuilder,
            openAi,
            payloadStore,
            publisher);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var runTask = worker.RunAsync(cts.Token);

        await published.Task;
        cts.Cancel();
        await runTask;

        await sqs.Received(1).DeleteMessageAsync(
            options.Value.InputQueueUrl,
            "rh-1",
            Arg.Any<CancellationToken>());
        await dedupe.Received(1).MarkCompletedAsync("job-1", "attempt-1", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAsync_RejectsUnsupportedTaskType()
    {
        var logger = Substitute.For<ILogger<Worker>>();
        var sqs = Substitute.For<IQueueClient>();
        var options = CreateOptions();
        var dedupe = Substitute.For<IDedupeStore>();
        var templateStore = Substitute.For<IPromptTemplateStore>();
        var promptBuilder = Substitute.For<IPromptBuilder>();
        var openAi = Substitute.For<IOpenAiClient>();
        var payloadStore = Substitute.For<IResultPayloadStore>();
        var publisher = Substitute.For<IResultPublisher>();

        var message = new QueueMessage
        {
            Body = """
                   { "job_id": "job-2", "attempt_id": "attempt-2", "task_type": "image", "prompt_s3_key": "prompts/job-2.txt" }
                   """,
            ReceiptHandle = "rh-2"
        };

        sqs.ReceiveMessageAsync(Arg.Any<QueueReceiveRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new QueueReceiveResult { Messages = new List<QueueMessage> { message } },
                new QueueReceiveResult());

        dedupe.TryStartAsync("job-2", "attempt-2", Arg.Any<CancellationToken>())
            .Returns(DedupeDecision.Started);

        var published = new TaskCompletionSource();
        publisher.PublishAsync(Arg.Any<ResultEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                published.SetResult();
                return Task.CompletedTask;
            });

        var worker = new Worker(
            logger,
            sqs,
            options,
            dedupe,
            templateStore,
            promptBuilder,
            openAi,
            payloadStore,
            publisher);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var runTask = worker.RunAsync(cts.Token);

        await published.Task;
        cts.Cancel();
        await runTask;

        await openAi.DidNotReceive()
            .ExecuteAsync(Arg.Any<CanonicalJobRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await sqs.Received(1).DeleteMessageAsync(
            options.Value.InputQueueUrl,
            "rh-2",
            Arg.Any<CancellationToken>());
        await dedupe.Received(1).MarkCompletedAsync("job-2", "attempt-2", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAsync_DeletesInvalidPayload()
    {
        var logger = Substitute.For<ILogger<Worker>>();
        var sqs = Substitute.For<IQueueClient>();
        var options = CreateOptions();
        var dedupe = Substitute.For<IDedupeStore>();
        var templateStore = Substitute.For<IPromptTemplateStore>();
        var promptBuilder = Substitute.For<IPromptBuilder>();
        var openAi = Substitute.For<IOpenAiClient>();
        var payloadStore = Substitute.For<IResultPayloadStore>();
        var publisher = Substitute.For<IResultPublisher>();

        var message = new QueueMessage
        {
            Body = """{ "job_id": "", "attempt_id": "" }""",
            ReceiptHandle = "rh-3"
        };

        sqs.ReceiveMessageAsync(Arg.Any<QueueReceiveRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new QueueReceiveResult { Messages = new List<QueueMessage> { message } },
                new QueueReceiveResult());

        var worker = new Worker(
            logger,
            sqs,
            options,
            dedupe,
            templateStore,
            promptBuilder,
            openAi,
            payloadStore,
            publisher);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var runTask = worker.RunAsync(cts.Token);

        await Task.Delay(100);
        cts.Cancel();
        await runTask;

        await sqs.Received(1).DeleteMessageAsync(
            options.Value.InputQueueUrl,
            "rh-3",
            Arg.Any<CancellationToken>());
        await dedupe.DidNotReceive()
            .TryStartAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await publisher.DidNotReceive()
            .PublishAsync(Arg.Any<ResultEvent>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAsync_DedupedMessageIsDeleted()
    {
        var logger = Substitute.For<ILogger<Worker>>();
        var sqs = Substitute.For<IQueueClient>();
        var options = CreateOptions();
        var dedupe = Substitute.For<IDedupeStore>();
        var templateStore = Substitute.For<IPromptTemplateStore>();
        var promptBuilder = Substitute.For<IPromptBuilder>();
        var openAi = Substitute.For<IOpenAiClient>();
        var payloadStore = Substitute.For<IResultPayloadStore>();
        var publisher = Substitute.For<IResultPublisher>();

        var message = new QueueMessage
        {
            Body = """
                   { "job_id": "job-4", "attempt_id": "attempt-4", "task_type": "chat_completion", "prompt_s3_key": "prompts/job-4.txt" }
                   """,
            ReceiptHandle = "rh-4"
        };

        sqs.ReceiveMessageAsync(Arg.Any<QueueReceiveRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new QueueReceiveResult { Messages = new List<QueueMessage> { message } },
                new QueueReceiveResult());

        dedupe.TryStartAsync("job-4", "attempt-4", Arg.Any<CancellationToken>())
            .Returns(DedupeDecision.DuplicateCompleted);

        var worker = new Worker(
            logger,
            sqs,
            options,
            dedupe,
            templateStore,
            promptBuilder,
            openAi,
            payloadStore,
            publisher);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var runTask = worker.RunAsync(cts.Token);

        await Task.Delay(100);
        cts.Cancel();
        await runTask;

        await sqs.Received(1).DeleteMessageAsync(
            options.Value.InputQueueUrl,
            "rh-4",
            Arg.Any<CancellationToken>());
        await publisher.DidNotReceive()
            .PublishAsync(Arg.Any<ResultEvent>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAsync_PublishesErrorWhenPromptLoadFails()
    {
        var logger = Substitute.For<ILogger<Worker>>();
        var sqs = Substitute.For<IQueueClient>();
        var options = CreateOptions();
        var dedupe = Substitute.For<IDedupeStore>();
        var templateStore = Substitute.For<IPromptTemplateStore>();
        var promptBuilder = Substitute.For<IPromptBuilder>();
        var openAi = Substitute.For<IOpenAiClient>();
        var payloadStore = Substitute.For<IResultPayloadStore>();
        var publisher = Substitute.For<IResultPublisher>();

        var message = new QueueMessage
        {
            Body = """
                   { "job_id": "job-5", "attempt_id": "attempt-5", "task_type": "chat_completion", "prompt_s3_key": "prompts/job-5.txt" }
                   """,
            ReceiptHandle = "rh-5"
        };

        sqs.ReceiveMessageAsync(Arg.Any<QueueReceiveRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new QueueReceiveResult { Messages = new List<QueueMessage> { message } },
                new QueueReceiveResult());

        dedupe.TryStartAsync("job-5", "attempt-5", Arg.Any<CancellationToken>())
            .Returns(DedupeDecision.Started);
        templateStore.GetTemplateAsync(Arg.Any<CanonicalJobRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new InvalidOperationException("boom")));

        ResultEvent? captured = null;
        publisher.PublishAsync(Arg.Do<ResultEvent>(evt => captured = evt), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var worker = new Worker(
            logger,
            sqs,
            options,
            dedupe,
            templateStore,
            promptBuilder,
            openAi,
            payloadStore,
            publisher);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var runTask = worker.RunAsync(cts.Token);

        await Task.Delay(100);
        cts.Cancel();
        await runTask;

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Status, Is.EqualTo("failed"));
        await openAi.DidNotReceive()
            .ExecuteAsync(Arg.Any<CanonicalJobRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await sqs.Received(1).DeleteMessageAsync(
            options.Value.InputQueueUrl,
            "rh-5",
            Arg.Any<CancellationToken>());
        await dedupe.Received(1).MarkCompletedAsync("job-5", "attempt-5", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAsync_DoesNotDeleteMessageWhenPublishFails()
    {
        var logger = Substitute.For<ILogger<Worker>>();
        var sqs = Substitute.For<IQueueClient>();
        var options = CreateOptions();
        var dedupe = Substitute.For<IDedupeStore>();
        var templateStore = Substitute.For<IPromptTemplateStore>();
        var promptBuilder = Substitute.For<IPromptBuilder>();
        var openAi = Substitute.For<IOpenAiClient>();
        var payloadStore = Substitute.For<IResultPayloadStore>();
        var publisher = Substitute.For<IResultPublisher>();

        var message = new QueueMessage
        {
            Body = """
                   { "job_id": "job-6", "attempt_id": "attempt-6", "task_type": "chat_completion", "prompt_s3_key": "prompts/job-6.txt" }
                   """,
            ReceiptHandle = "rh-6"
        };

        sqs.ReceiveMessageAsync(Arg.Any<QueueReceiveRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new QueueReceiveResult { Messages = new List<QueueMessage> { message } },
                new QueueReceiveResult());

        dedupe.TryStartAsync("job-6", "attempt-6", Arg.Any<CancellationToken>())
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

        var worker = new Worker(
            logger,
            sqs,
            options,
            dedupe,
            templateStore,
            promptBuilder,
            openAi,
            payloadStore,
            publisher);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var runTask = worker.RunAsync(cts.Token);

        await Task.Delay(100);
        cts.Cancel();
        await runTask;

        await sqs.DidNotReceive().DeleteMessageAsync(
            options.Value.InputQueueUrl,
            "rh-6",
            Arg.Any<CancellationToken>());
        await dedupe.DidNotReceive()
            .MarkCompletedAsync("job-6", "attempt-6", Arg.Any<CancellationToken>());
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
            VisibilityTimeoutSeconds = 0
        });
    }
}
