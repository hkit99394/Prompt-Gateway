using System.Net;
using System.Text;
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

    private sealed class ControlPlaneApiFactory : WebApplicationFactory<Program>
    {
        public const string ValidApiKey = "test-api-key";
        public const string RotatedApiKey = "next-test-api-key";
        public IJobStore JobStore { get; } = Substitute.For<IJobStore>();

        public ControlPlaneApiFactory()
        {
            Environment.SetEnvironmentVariable("ApiSecurity__ApiKey", ValidApiKey);
            Environment.SetEnvironmentVariable("ApiSecurity__ApiKeys__0", ValidApiKey);
            Environment.SetEnvironmentVariable("ApiSecurity__ApiKeys__1", RotatedApiKey);
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
                    ["Retry:MaxAttempts"] = "3"
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

                JobStore.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<IReadOnlyList<JobSummary>>(Array.Empty<JobSummary>()));

                services.AddSingleton(JobStore);
                services.AddSingleton(Substitute.For<IJobEventStore>());
                services.AddSingleton(Substitute.For<IOutboxStore>());
                services.AddSingleton(Substitute.For<IDeduplicationStore>());
                services.AddSingleton(Substitute.For<IResultStore>());
            });
        }
    }
}
