using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ControlPlane.Core.Tests;

public class JobOrchestratorTests
{
    [Test]
    public async Task AcceptAsync_GeneratesIdsAndPersistsCreatedEvent()
    {
        var jobStore = Substitute.For<IJobStore>();
        var eventStore = Substitute.For<IJobEventStore>();
        var routingPolicy = Substitute.For<IRoutingPolicy>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var dedupeStore = Substitute.For<IDeduplicationStore>();
        var assembler = Substitute.For<ICanonicalResponseAssembler>();
        var resultStore = Substitute.For<IResultStore>();
        var retryPlanner = Substitute.For<IRetryPlanner>();
        var idGenerator = Substitute.For<IIdGenerator>();
        var clock = Substitute.For<IClock>();
        var logger = Substitute.For<ILogger<JobOrchestrator>>();

        idGenerator.NewId("job").Returns("job-1");
        idGenerator.NewId("attempt").Returns("attempt-1");
        idGenerator.NewTraceId().Returns("trace-1");
        clock.UtcNow.Returns(new DateTimeOffset(2026, 2, 8, 0, 0, 0, TimeSpan.Zero));

        var orchestrator = new JobOrchestrator(
            logger,
            jobStore,
            eventStore,
            routingPolicy,
            outboxStore,
            dedupeStore,
            assembler,
            resultStore,
            retryPlanner,
            idGenerator,
            clock);

        var request = new CanonicalJobRequest { TaskType = "chat_completion" };

        var handle = await orchestrator.AcceptAsync(request, CancellationToken.None);

        await jobStore.Received(1).CreateAsync(
            Arg.Is<JobRecord>(job => job.JobId == "job-1" && job.State == JobState.Created),
            Arg.Any<CancellationToken>());

        await eventStore.Received(1).AppendAsync(
            Arg.Is<JobEvent>(evt => evt.Type == JobEventType.Created && evt.JobId == "job-1"),
            Arg.Any<CancellationToken>());

        Assert.That(handle.JobId, Is.EqualTo("job-1"));
        Assert.That(handle.AttemptId, Is.EqualTo("attempt-1"));
        Assert.That(handle.TraceId, Is.EqualTo("trace-1"));
    }

    [Test]
    public async Task RouteAsync_PersistsDecisionAndUpdatesState()
    {
        var jobStore = Substitute.For<IJobStore>();
        var eventStore = Substitute.For<IJobEventStore>();
        var routingPolicy = Substitute.For<IRoutingPolicy>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var dedupeStore = Substitute.For<IDeduplicationStore>();
        var assembler = Substitute.For<ICanonicalResponseAssembler>();
        var resultStore = Substitute.For<IResultStore>();
        var retryPlanner = Substitute.For<IRetryPlanner>();
        var idGenerator = Substitute.For<IIdGenerator>();
        var clock = Substitute.For<IClock>();
        var logger = Substitute.For<ILogger<JobOrchestrator>>();

        var now = new DateTimeOffset(2026, 2, 8, 0, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(now);

        var request = new CanonicalJobRequest
        {
            JobId = "job-2",
            AttemptId = "attempt-2",
            TraceId = "trace-2",
            TaskType = "chat_completion"
        };

        var job = JobRecord.Create(request, now);
        jobStore.GetAsync("job-2", Arg.Any<CancellationToken>()).Returns(job);

        routingPolicy.DecideAsync(request, Arg.Any<CancellationToken>())
            .Returns(new RoutingDecision
            {
                Provider = "openai",
                Model = "gpt-4.1",
                PolicyVersion = "v1"
            });

        var orchestrator = new JobOrchestrator(
            logger,
            jobStore,
            eventStore,
            routingPolicy,
            outboxStore,
            dedupeStore,
            assembler,
            resultStore,
            retryPlanner,
            idGenerator,
            clock);

        var decision = await orchestrator.RouteAsync("job-2", CancellationToken.None);

        await jobStore.Received(1).UpdateAsync(
            Arg.Is<JobRecord>(record => record.State == JobState.Routed),
            Arg.Any<CancellationToken>());

        await eventStore.Received(1).AppendAsync(
            Arg.Is<JobEvent>(evt => evt.Type == JobEventType.Routed && evt.Attributes!["provider"] == "openai"),
            Arg.Any<CancellationToken>());

        Assert.That(decision.Provider, Is.EqualTo("openai"));
        Assert.That(job.GetAttempt("attempt-2")!.State, Is.EqualTo(AttemptState.Routed));
    }

    [Test]
    public async Task DispatchAsync_EnqueuesOutboxAndUpdatesState()
    {
        var jobStore = Substitute.For<IJobStore>();
        var eventStore = Substitute.For<IJobEventStore>();
        var routingPolicy = Substitute.For<IRoutingPolicy>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var dedupeStore = Substitute.For<IDeduplicationStore>();
        var assembler = Substitute.For<ICanonicalResponseAssembler>();
        var resultStore = Substitute.For<IResultStore>();
        var retryPlanner = Substitute.For<IRetryPlanner>();
        var idGenerator = Substitute.For<IIdGenerator>();
        var clock = Substitute.For<IClock>();
        var logger = Substitute.For<ILogger<JobOrchestrator>>();

        idGenerator.NewId("outbox").Returns("outbox-1");
        var now = new DateTimeOffset(2026, 2, 8, 0, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(now);

        var request = new CanonicalJobRequest
        {
            JobId = "job-3",
            AttemptId = "attempt-3",
            TraceId = "trace-3",
            TaskType = "chat_completion"
        };

        var job = JobRecord.Create(request, now);
        var attempt = job.GetAttempt("attempt-3")!;
        attempt.ApplyRouting(new RoutingDecision
        {
            Provider = "openai",
            Model = "gpt-4.1",
            PolicyVersion = "v1"
        }, now);

        jobStore.GetAsync("job-3", Arg.Any<CancellationToken>()).Returns(job);

        var orchestrator = new JobOrchestrator(
            logger,
            jobStore,
            eventStore,
            routingPolicy,
            outboxStore,
            dedupeStore,
            assembler,
            resultStore,
            retryPlanner,
            idGenerator,
            clock);

        var dispatch = await orchestrator.DispatchAsync("job-3", "attempt-3", CancellationToken.None);

        await outboxStore.Received(1).EnqueueDispatchAsync(
            Arg.Is<OutboxDispatchMessage>(message => message.OutboxId == "outbox-1"),
            Arg.Any<CancellationToken>());

        await jobStore.Received(1).UpdateAsync(
            Arg.Is<JobRecord>(record => record.State == JobState.Dispatched),
            Arg.Any<CancellationToken>());

        await eventStore.Received(1).AppendAsync(
            Arg.Is<JobEvent>(evt => evt.Type == JobEventType.Dispatched),
            Arg.Any<CancellationToken>());

        Assert.That(dispatch.IdempotencyKey, Is.EqualTo("job-3:attempt-3"));
    }

    [Test]
    public async Task IngestResultAsync_IgnoresDuplicateAttempt()
    {
        var jobStore = Substitute.For<IJobStore>();
        var eventStore = Substitute.For<IJobEventStore>();
        var routingPolicy = Substitute.For<IRoutingPolicy>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var dedupeStore = Substitute.For<IDeduplicationStore>();
        var assembler = Substitute.For<ICanonicalResponseAssembler>();
        var resultStore = Substitute.For<IResultStore>();
        var retryPlanner = Substitute.For<IRetryPlanner>();
        var idGenerator = Substitute.For<IIdGenerator>();
        var clock = Substitute.For<IClock>();
        var logger = Substitute.For<ILogger<JobOrchestrator>>();

        dedupeStore.TryStartAsync("job-4", "attempt-4", Arg.Any<CancellationToken>())
            .Returns(false);

        var orchestrator = new JobOrchestrator(
            logger,
            jobStore,
            eventStore,
            routingPolicy,
            outboxStore,
            dedupeStore,
            assembler,
            resultStore,
            retryPlanner,
            idGenerator,
            clock);

        var outcome = await orchestrator.IngestResultAsync(new ProviderResultEvent
        {
            JobId = "job-4",
            AttemptId = "attempt-4",
            Provider = "openai",
            Model = "gpt-4.1",
            IsSuccess = true
        }, CancellationToken.None);

        Assert.That(outcome.Status, Is.EqualTo(ResultIngestionStatus.Duplicate));
        await jobStore.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await eventStore.DidNotReceive().AppendAsync(Arg.Any<JobEvent>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task IngestResultAsync_FinalizesOnSuccess()
    {
        var jobStore = Substitute.For<IJobStore>();
        var eventStore = Substitute.For<IJobEventStore>();
        var routingPolicy = Substitute.For<IRoutingPolicy>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var dedupeStore = Substitute.For<IDeduplicationStore>();
        var assembler = Substitute.For<ICanonicalResponseAssembler>();
        var resultStore = Substitute.For<IResultStore>();
        var retryPlanner = Substitute.For<IRetryPlanner>();
        var idGenerator = Substitute.For<IIdGenerator>();
        var clock = Substitute.For<IClock>();
        var logger = Substitute.For<ILogger<JobOrchestrator>>();

        var now = new DateTimeOffset(2026, 2, 8, 0, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(now);

        var request = new CanonicalJobRequest
        {
            JobId = "job-5",
            AttemptId = "attempt-5",
            TraceId = "trace-5",
            TaskType = "chat_completion"
        };

        var job = JobRecord.Create(request, now);
        jobStore.GetAsync("job-5", Arg.Any<CancellationToken>()).Returns(job);
        dedupeStore.TryStartAsync("job-5", "attempt-5", Arg.Any<CancellationToken>())
            .Returns(true);

        var response = new CanonicalResponse
        {
            Provider = "openai",
            Model = "gpt-4.1"
        };

        assembler.AssembleAsync(Arg.Any<ProviderResultEvent>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var orchestrator = new JobOrchestrator(
            logger,
            jobStore,
            eventStore,
            routingPolicy,
            outboxStore,
            dedupeStore,
            assembler,
            resultStore,
            retryPlanner,
            idGenerator,
            clock);

        var outcome = await orchestrator.IngestResultAsync(new ProviderResultEvent
        {
            JobId = "job-5",
            AttemptId = "attempt-5",
            Provider = "openai",
            Model = "gpt-4.1",
            IsSuccess = true
        }, CancellationToken.None);

        Assert.That(outcome.Status, Is.EqualTo(ResultIngestionStatus.Finalized));
        await resultStore.Received(1).SaveFinalResultAsync("job-5", response, Arg.Any<CancellationToken>());
        await eventStore.Received(1).AppendAsync(
            Arg.Is<JobEvent>(evt => evt.Type == JobEventType.Completed),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task IngestResultAsync_RetriesWhenPlannerRequestsFallback()
    {
        var jobStore = Substitute.For<IJobStore>();
        var eventStore = Substitute.For<IJobEventStore>();
        var routingPolicy = Substitute.For<IRoutingPolicy>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var dedupeStore = Substitute.For<IDeduplicationStore>();
        var assembler = Substitute.For<ICanonicalResponseAssembler>();
        var resultStore = Substitute.For<IResultStore>();
        var retryPlanner = Substitute.For<IRetryPlanner>();
        var idGenerator = Substitute.For<IIdGenerator>();
        var clock = Substitute.For<IClock>();
        var logger = Substitute.For<ILogger<JobOrchestrator>>();

        var now = new DateTimeOffset(2026, 2, 8, 0, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(now);
        dedupeStore.TryStartAsync("job-6", "attempt-6", Arg.Any<CancellationToken>())
            .Returns(true);
        idGenerator.NewId("attempt").Returns("attempt-6b");
        idGenerator.NewId("outbox").Returns("outbox-6");

        var request = new CanonicalJobRequest
        {
            JobId = "job-6",
            AttemptId = "attempt-6",
            TraceId = "trace-6",
            TaskType = "chat_completion"
        };

        var job = JobRecord.Create(request, now);
        jobStore.GetAsync("job-6", Arg.Any<CancellationToken>()).Returns(job);

        retryPlanner.PlanRetry(job, Arg.Any<JobAttempt>(), Arg.Any<ProviderResultEvent>())
            .Returns(RetryPlan.ForProvider("anthropic", "claude-3", "fallback"));

        var orchestrator = new JobOrchestrator(
            logger,
            jobStore,
            eventStore,
            routingPolicy,
            outboxStore,
            dedupeStore,
            assembler,
            resultStore,
            retryPlanner,
            idGenerator,
            clock);

        var outcome = await orchestrator.IngestResultAsync(new ProviderResultEvent
        {
            JobId = "job-6",
            AttemptId = "attempt-6",
            Provider = "openai",
            Model = "gpt-4.1",
            IsSuccess = false,
            Error = new CanonicalError("timeout", "Provider timeout")
        }, CancellationToken.None);

        Assert.That(outcome.Status, Is.EqualTo(ResultIngestionStatus.Retrying));
        await outboxStore.Received(1).EnqueueDispatchAsync(
            Arg.Is<OutboxDispatchMessage>(message => message.Message.AttemptId == "attempt-6b"),
            Arg.Any<CancellationToken>());
        await eventStore.Received(1).AppendAsync(
            Arg.Is<JobEvent>(evt => evt.Type == JobEventType.Retried),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ListJobsAsync_ReturnsSummaries()
    {
        var jobStore = Substitute.For<IJobStore>();
        var eventStore = Substitute.For<IJobEventStore>();
        var routingPolicy = Substitute.For<IRoutingPolicy>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var dedupeStore = Substitute.For<IDeduplicationStore>();
        var assembler = Substitute.For<ICanonicalResponseAssembler>();
        var resultStore = Substitute.For<IResultStore>();
        var retryPlanner = Substitute.For<IRetryPlanner>();
        var idGenerator = Substitute.For<IIdGenerator>();
        var clock = Substitute.For<IClock>();
        var logger = Substitute.For<ILogger<JobOrchestrator>>();

        var summaries = new[]
        {
            new JobSummary("job-9", "trace-9", "attempt-9", JobState.Completed, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow)
        };

        jobStore.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(summaries);

        var orchestrator = new JobOrchestrator(
            logger,
            jobStore,
            eventStore,
            routingPolicy,
            outboxStore,
            dedupeStore,
            assembler,
            resultStore,
            retryPlanner,
            idGenerator,
            clock);

        var result = await orchestrator.ListJobsAsync(10, CancellationToken.None);

        Assert.That(result, Is.EqualTo(summaries));
        await jobStore.Received(1).ListAsync(10, Arg.Any<CancellationToken>());
    }
}
