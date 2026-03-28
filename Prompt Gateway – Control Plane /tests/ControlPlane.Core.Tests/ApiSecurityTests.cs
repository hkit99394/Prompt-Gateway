using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using ControlPlane.Api;
using ControlPlane.Api.Auth;
using ControlPlane.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace ControlPlane.Core.Tests;

public class ApiSecurityTests
{
    [Test]
    public async Task GetJobs_WithoutApiKey_ReturnsUnauthorized()
    {
        await using var factory = new ControlPlaneApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/jobs?limit=1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task GetJobs_WithInvalidApiKey_ReturnsUnauthorized()
    {
        await using var factory = new ControlPlaneApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, "wrong-key");

        var response = await client.GetAsync("/jobs?limit=1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task GetJobs_WithValidApiKey_ReturnsOk()
    {
        await using var factory = new ControlPlaneApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ControlPlaneApiFactory.ValidApiKey);

        var response = await client.GetAsync("/jobs?limit=1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetJobs_WithRotatedApiKey_ReturnsOk()
    {
        await using var factory = new ControlPlaneApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ControlPlaneApiFactory.RotatedApiKey);

        var response = await client.GetAsync("/jobs?limit=1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task CreateJob_MissingTaskType_ReturnsBadRequest()
    {
        await using var factory = new ControlPlaneApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ControlPlaneApiFactory.ValidApiKey);

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/jobs", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateJob_MissingPromptReference_ReturnsBadRequest()
    {
        await using var factory = new ControlPlaneApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ControlPlaneApiFactory.ValidApiKey);

        using var content = new StringContent("""{"taskType":"chat_completion"}""", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/jobs", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateJob_WithValidRequest_ReturnsAccepted()
    {
        await using var factory = new ControlPlaneApiFactory();
        factory.EnableInMemoryJobPersistence();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ControlPlaneApiFactory.ValidApiKey);

        using var content = new StringContent(
            """{"taskType":"chat_completion","promptKey":"prompts/job-accepted.txt"}""",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/jobs", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That(response.Headers.Location, Is.Not.Null);
        Assert.That(response.Headers.Location!.OriginalString, Does.Contain("/jobs/"));
        using var json = await ReadJsonAsync(response);
        Assert.That(json.RootElement.GetProperty("accepted").GetBoolean(), Is.True);
        Assert.That(json.RootElement.GetProperty("replayed").GetBoolean(), Is.False);
        Assert.That(json.RootElement.GetProperty("requiresResume").GetBoolean(), Is.True);
        Assert.That(json.RootElement.GetProperty("state").GetString(), Is.EqualTo(JobState.Created.ToString()));
        Assert.That(json.RootElement.GetProperty("idempotencyKey").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task CreateJob_WithInlinePrompt_ReturnsAccepted()
    {
        await using var factory = new ControlPlaneApiFactory();
        factory.EnableInMemoryJobPersistence();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ControlPlaneApiFactory.ValidApiKey);

        using var content = new StringContent(
            """{"taskType":"chat_completion","promptText":"Explain vector databases simply."}""",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/jobs", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        using var json = await ReadJsonAsync(response);
        Assert.That(json.RootElement.GetProperty("accepted").GetBoolean(), Is.True);
        Assert.That(json.RootElement.GetProperty("requiresResume").GetBoolean(), Is.True);
    }

    [Test]
    public async Task CreateJob_DoesNotRouteOrDispatchInline()
    {
        await using var factory = new ControlPlaneApiFactory();
        factory.EnableInMemoryJobPersistence();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ControlPlaneApiFactory.ValidApiKey);

        using var content = new StringContent(
            """{"taskType":"chat_completion","promptKey":"prompts/job-resume-needed.txt"}""",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/jobs", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        await factory.JobStore.DidNotReceive().UpdateAsync(Arg.Any<JobRecord>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await factory.OutboxStore.DidNotReceive().EnqueueDispatchAsync(Arg.Any<OutboxDispatchMessage>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateJob_WithIdempotencyKey_ReplaysExistingAcceptedJob()
    {
        await using var factory = new ControlPlaneApiFactory();
        factory.EnableInMemoryJobPersistence();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ControlPlaneApiFactory.ValidApiKey);
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "same-request");

        using var content = new StringContent(
            """{"taskType":"chat_completion","promptKey":"prompts/idempotent.txt"}""",
            Encoding.UTF8,
            "application/json");
        using var replayContent = new StringContent(
            """{"taskType":"chat_completion","promptKey":"prompts/idempotent.txt"}""",
            Encoding.UTF8,
            "application/json");

        var first = await client.PostAsync("/jobs", content);
        using var firstJson = await ReadJsonAsync(first);
        var firstJobId = firstJson.RootElement.GetProperty("jobId").GetString();
        var firstAttemptId = firstJson.RootElement.GetProperty("attemptId").GetString();

        var second = await client.PostAsync("/jobs", replayContent);

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        using var secondJson = await ReadJsonAsync(second);
        Assert.That(secondJson.RootElement.GetProperty("replayed").GetBoolean(), Is.True);
        Assert.That(secondJson.RootElement.GetProperty("requiresResume").GetBoolean(), Is.True);
        Assert.That(secondJson.RootElement.GetProperty("jobId").GetString(), Is.EqualTo(firstJobId));
        Assert.That(secondJson.RootElement.GetProperty("attemptId").GetString(), Is.EqualTo(firstAttemptId));
        await factory.JobStore.Received(1).CreateAsync(Arg.Any<JobRecord>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateJob_IdempotentReplay_ReturnsSamePendingHandle()
    {
        await using var factory = new ControlPlaneApiFactory();
        factory.EnableInMemoryJobPersistence();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ControlPlaneApiFactory.ValidApiKey);
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "resume-on-replay");

        using var firstContent = new StringContent(
            """{"taskType":"chat_completion","promptKey":"prompts/resume-on-replay.txt"}""",
            Encoding.UTF8,
            "application/json");
        var first = await client.PostAsync("/jobs", firstContent);
        using var firstJson = await ReadJsonAsync(first);
        Assert.That(firstJson.RootElement.GetProperty("requiresResume").GetBoolean(), Is.True);
        var firstJobId = firstJson.RootElement.GetProperty("jobId").GetString();
        var firstAttemptId = firstJson.RootElement.GetProperty("attemptId").GetString();

        using var replayContent = new StringContent(
            """{"taskType":"chat_completion","promptKey":"prompts/resume-on-replay.txt"}""",
            Encoding.UTF8,
            "application/json");
        var replay = await client.PostAsync("/jobs", replayContent);

        Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        using var replayJson = await ReadJsonAsync(replay);
        Assert.That(replayJson.RootElement.GetProperty("replayed").GetBoolean(), Is.True);
        Assert.That(replayJson.RootElement.GetProperty("requiresResume").GetBoolean(), Is.True);
        Assert.That(replayJson.RootElement.GetProperty("jobId").GetString(), Is.EqualTo(firstJobId));
        Assert.That(replayJson.RootElement.GetProperty("attemptId").GetString(), Is.EqualTo(firstAttemptId));
    }

    [Test]
    public async Task CreateJob_WithSameIdempotencyKeyAndDifferentPayload_ReturnsConflict()
    {
        await using var factory = new ControlPlaneApiFactory();
        factory.EnableInMemoryJobPersistence();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ControlPlaneApiFactory.ValidApiKey);
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "same-request");

        using var firstContent = new StringContent(
            """{"taskType":"chat_completion","promptKey":"prompts/idempotent-a.txt"}""",
            Encoding.UTF8,
            "application/json");
        using var secondContent = new StringContent(
            """{"taskType":"chat_completion","promptKey":"prompts/idempotent-b.txt"}""",
            Encoding.UTF8,
            "application/json");

        var first = await client.PostAsync("/jobs", firstContent);
        var second = await client.PostAsync("/jobs", secondContent);

        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task ListJobs_LimitGreaterThanMaximum_ClampsToMaximum()
    {
        await using var factory = new ControlPlaneApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ControlPlaneApiFactory.ValidApiKey);

        var response = await client.GetAsync("/jobs?limit=1000000");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await factory.JobStore.Received(1).ListAsync(200, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResumeJob_ForCreatedJob_ResumesRoutingAndDispatch()
    {
        await using var factory = new ControlPlaneApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ControlPlaneApiFactory.ValidApiKey);

        var createdAt = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero);
        var job = JobRecord.Create(new CanonicalJobRequest
        {
            JobId = "job-resume-1",
            AttemptId = "attempt-resume-1",
            TraceId = "trace-resume-1",
            TaskType = "chat_completion"
        }, createdAt);
        factory.JobStore.GetAsync("job-resume-1", Arg.Any<CancellationToken>()).Returns(job);

        var response = await client.PostAsync("/jobs/job-resume-1/resume", content: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ResumeJob_ForTerminalState_ReturnsConflict()
    {
        await using var factory = new ControlPlaneApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ControlPlaneApiFactory.ValidApiKey);

        var createdAt = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero);
        var job = JobRecord.Create(new CanonicalJobRequest
        {
            JobId = "job-resume-2",
            AttemptId = "attempt-resume-2",
            TraceId = "trace-resume-2",
            TaskType = "chat_completion"
        }, createdAt);
        job.SetState(JobState.Routed, createdAt.AddMinutes(1));
        job.SetState(JobState.Dispatched, createdAt.AddMinutes(2));
        job.SetState(JobState.Completed, createdAt.AddMinutes(3));
        factory.JobStore.GetAsync("job-resume-2", Arg.Any<CancellationToken>()).Returns(job);

        var response = await client.PostAsync("/jobs/job-resume-2/resume", content: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task HealthEndpoint_WithoutApiKey_ReturnsOk()
    {
        await using var factory = new ControlPlaneApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ReadyEndpoint_WithoutApiKey_ReturnsOk()
    {
        await using var factory = new ControlPlaneApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/ready");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SwaggerJsonEndpoint_WithoutApiKey_ReturnsOk()
    {
        await using var factory = new ControlPlaneApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SwaggerJsonEndpoint_WhenDisabled_ReturnsNotFound()
    {
        await using var factory = new ControlPlaneApiFactory(enableSwagger: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private sealed class ControlPlaneApiFactory : WebApplicationFactory<Program>
    {
        public const string ValidApiKey = "test-api-key";
        public const string RotatedApiKey = "next-test-api-key";
        public IJobStore JobStore { get; } = Substitute.For<IJobStore>();
        public IJobEventStore JobEventStore { get; } = Substitute.For<IJobEventStore>();
        public IOutboxStore OutboxStore { get; } = Substitute.For<IOutboxStore>();
        public IAmazonDynamoDB DynamoDb { get; } = Substitute.For<IAmazonDynamoDB>();
        public IAmazonSQS Sqs { get; } = Substitute.For<IAmazonSQS>();
        private readonly bool _enableSwagger;
        private readonly Dictionary<string, JobRecord> _jobs = new(StringComparer.Ordinal);
        private Exception? _jobUpdateFailure;

        public ControlPlaneApiFactory(bool enableSwagger = true)
        {
            _enableSwagger = enableSwagger;
            Environment.SetEnvironmentVariable("ApiSecurity__ApiKey", ValidApiKey);
            Environment.SetEnvironmentVariable("ApiSecurity__ApiKeys__0", ValidApiKey);
            Environment.SetEnvironmentVariable("ApiSecurity__ApiKeys__1", RotatedApiKey);
            Environment.SetEnvironmentVariable("AwsStorage__TableName", "test-table");
            Environment.SetEnvironmentVariable("AwsQueue__DispatchQueueUrl", "https://sqs.us-east-1.amazonaws.com/123456789012/test-dispatch");
            Environment.SetEnvironmentVariable("AwsQueue__ResultQueueUrl", "https://sqs.us-east-1.amazonaws.com/123456789012/test-result");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Routing:Provider"] = "openai",
                    ["Routing:Model"] = "gpt-4.1",
                    ["Routing:PolicyVersion"] = "test",
                    ["Retry:MaxAttempts"] = "3",
                    ["ControlPlaneApi:EnableSwagger"] = _enableSwagger ? "true" : "false",
                    ["HostedWorkers:EnablePostAcceptResumeWorker"] = "false",
                    ["HostedWorkers:EnableOutboxWorker"] = "false",
                    ["HostedWorkers:EnableResultQueueWorker"] = "false",
                    ["AwsStorage:TableName"] = "test-table",
                    ["AwsQueue:DispatchQueueUrl"] = "https://sqs.us-east-1.amazonaws.com/123456789012/test-dispatch",
                    ["AwsQueue:ResultQueueUrl"] = "https://sqs.us-east-1.amazonaws.com/123456789012/test-result"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IPostAcceptResumeScheduler>();
                services.RemoveAll<PostAcceptResumeQueue>();
                services.RemoveAll<IJobStore>();
                services.RemoveAll<IJobEventStore>();
                services.RemoveAll<IOutboxStore>();
                services.RemoveAll<IDeduplicationStore>();
                services.RemoveAll<IResultStore>();
                services.RemoveAll<IAmazonDynamoDB>();
                services.RemoveAll<IAmazonSQS>();
                services.RemoveAll<ControlPlaneApiHostOptions>();

                JobStore.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<IReadOnlyList<JobSummary>>(Array.Empty<JobSummary>()));
                DynamoDb.DescribeTableAsync(Arg.Any<DescribeTableRequest>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult(new DescribeTableResponse
                    {
                        Table = new TableDescription
                        {
                            GlobalSecondaryIndexes = new List<GlobalSecondaryIndexDescription>
                            {
                                new()
                                {
                                    IndexName = "gsi1"
                                }
                            }
                        }
                    }));
                Sqs.GetQueueAttributesAsync(Arg.Any<GetQueueAttributesRequest>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult(new GetQueueAttributesResponse()));

                services.AddSingleton(JobStore);
                services.AddSingleton(JobEventStore);
                services.AddSingleton(OutboxStore);
                services.AddSingleton(Substitute.For<IDeduplicationStore>());
                services.AddSingleton(Substitute.For<IResultStore>());
                services.AddSingleton(DynamoDb);
                services.AddSingleton(Sqs);
                services.AddSingleton<IPostAcceptResumeScheduler>(new ManualOnlyPostAcceptResumeScheduler());
                services.AddSingleton(new ControlPlaneApiHostOptions
                {
                    EnableSwagger = _enableSwagger,
                    HostedWorkers = new HostedWorkerOptions
                    {
                        EnablePostAcceptResumeWorker = false,
                        EnableOutboxWorker = false,
                        EnableResultQueueWorker = false
                    },
                    OutboxWorker = new OutboxWorkerOptions(),
                    ResultQueueWorker = new ResultQueueWorkerOptions()
                });
            });
        }

        public void EnableInMemoryJobPersistence()
        {
            JobStore.CreateAsync(Arg.Any<JobRecord>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var job = call.Arg<JobRecord>();
                    _jobs[job.JobId] = job;
                    return Task.CompletedTask;
                });

            JobStore.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var jobId = call.ArgAt<string>(0);
                    _jobs.TryGetValue(jobId, out var job);
                    return Task.FromResult(job);
                });

            JobStore.UpdateAsync(Arg.Any<JobRecord>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    if (_jobUpdateFailure is not null)
                    {
                        throw _jobUpdateFailure;
                    }

                    var job = call.Arg<JobRecord>();
                    _jobs[job.JobId] = job;
                    return Task.CompletedTask;
                });

            JobStore.UpdateAsync(Arg.Any<JobRecord>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    if (_jobUpdateFailure is not null)
                    {
                        throw _jobUpdateFailure;
                    }

                    var job = call.Arg<JobRecord>();
                    _jobs[job.JobId] = job;
                    return Task.CompletedTask;
                });

            JobStore.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var limit = call.ArgAt<int>(0);
                    IReadOnlyList<JobSummary> results = _jobs.Values
                        .Take(limit)
                        .Select(job => new JobSummary(
                            job.JobId,
                            job.TraceId,
                            job.CurrentAttemptId,
                            job.State,
                            job.CreatedAt,
                            job.UpdatedAt))
                        .ToArray();
                    return Task.FromResult(results);
                });
        }

        public void FailJobUpdateWith(Exception exception)
        {
            _jobUpdateFailure = exception;
        }

        public void ClearJobUpdateFailure()
        {
            _jobUpdateFailure = null;
        }

        private sealed class ManualOnlyPostAcceptResumeScheduler : IPostAcceptResumeScheduler
        {
            public bool TrySchedule(string jobId) => false;
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }
}
