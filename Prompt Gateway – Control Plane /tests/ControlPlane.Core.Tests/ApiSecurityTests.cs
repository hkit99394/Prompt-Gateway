using System.Net;
using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
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

    private sealed class ControlPlaneApiFactory : WebApplicationFactory<Program>
    {
        public const string ValidApiKey = "test-api-key";
        public const string RotatedApiKey = "next-test-api-key";
        public IJobStore JobStore { get; } = Substitute.For<IJobStore>();
        public IAmazonDynamoDB DynamoDb { get; } = Substitute.For<IAmazonDynamoDB>();
        public IAmazonSQS Sqs { get; } = Substitute.For<IAmazonSQS>();

        public ControlPlaneApiFactory()
        {
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
                    ["AwsStorage:TableName"] = "test-table",
                    ["AwsQueue:DispatchQueueUrl"] = "https://sqs.us-east-1.amazonaws.com/123456789012/test-dispatch",
                    ["AwsQueue:ResultQueueUrl"] = "https://sqs.us-east-1.amazonaws.com/123456789012/test-result"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IJobStore>();
                services.RemoveAll<IJobEventStore>();
                services.RemoveAll<IOutboxStore>();
                services.RemoveAll<IDeduplicationStore>();
                services.RemoveAll<IResultStore>();
                services.RemoveAll<IAmazonDynamoDB>();
                services.RemoveAll<IAmazonSQS>();

                JobStore.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<IReadOnlyList<JobSummary>>(Array.Empty<JobSummary>()));
                DynamoDb.DescribeTableAsync(Arg.Any<DescribeTableRequest>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult(new DescribeTableResponse()));
                Sqs.GetQueueAttributesAsync(Arg.Any<GetQueueAttributesRequest>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult(new GetQueueAttributesResponse()));

                services.AddSingleton(JobStore);
                services.AddSingleton(Substitute.For<IJobEventStore>());
                services.AddSingleton(Substitute.For<IOutboxStore>());
                services.AddSingleton(Substitute.For<IDeduplicationStore>());
                services.AddSingleton(Substitute.For<IResultStore>());
                services.AddSingleton(DynamoDb);
                services.AddSingleton(Sqs);
            });
        }
    }
}
