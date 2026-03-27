using System.Text.Json.Serialization;
using ControlPlane.Api.Auth;
using ControlPlane.Api.Health;
using ControlPlane.Aws;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ControlPlane.Api;

public static class ControlPlaneApiServiceCollectionExtensions
{
    public static IServiceCollection AddControlPlaneHttpApi(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ControlPlaneApiHostOptions>? configure = null)
    {
        var hostOptions = CreateHostOptions(configuration);
        configure?.Invoke(hostOptions);

        services.AddSingleton(hostOptions);
        services.AddSingleton(hostOptions.HostedWorkers);
        services.AddSingleton(hostOptions.OutboxWorker);
        services.AddSingleton(hostOptions.ResultQueueWorker);

        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services
            .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName,
                _ => { });
        services.AddAuthorization();
        services.AddControlPlaneRuntime(configuration);

        services.AddSingleton<PostAcceptResumeQueue>();
        services.AddSingleton<IPostAcceptResumeScheduler>(provider => provider.GetRequiredService<PostAcceptResumeQueue>());
        if (hostOptions.HostedWorkers.EnablePostAcceptResumeWorker)
        {
            services.AddHostedService<PostAcceptResumeWorker>();
        }

        if (hostOptions.HostedWorkers.EnableOutboxWorker)
        {
            services.AddHostedService<OutboxWorker>();
        }

        services.AddSingleton<ResultQueueProcessor>();
        if (hostOptions.HostedWorkers.EnableResultQueueWorker)
        {
            services.AddHostedService<ResultQueueWorker>();
        }

        services.AddHealthChecks()
            .AddCheck("live", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
            .AddCheck<AwsDependenciesHealthCheck>("aws_dependencies", tags: new[] { "ready" });

        if (ApiKeyConfiguration.GetConfiguredApiKeys(configuration).Count == 0)
        {
            throw new InvalidOperationException("At least one API key must be configured in ApiSecurity:ApiKeys or ApiSecurity:ApiKey.");
        }

        return services;
    }

    private static ControlPlaneApiHostOptions CreateHostOptions(IConfiguration configuration)
    {
        return new ControlPlaneApiHostOptions
        {
            EnableSwagger = !bool.TryParse(configuration["ControlPlaneApi:EnableSwagger"], out var enableSwagger) || enableSwagger,
            HostedWorkers = new HostedWorkerOptions
            {
                EnablePostAcceptResumeWorker = !bool.TryParse(configuration["HostedWorkers:EnablePostAcceptResumeWorker"], out var enablePostAcceptResumeWorker)
                    || enablePostAcceptResumeWorker,
                EnableOutboxWorker = !bool.TryParse(configuration["HostedWorkers:EnableOutboxWorker"], out var enableOutboxWorker)
                    || enableOutboxWorker,
                EnableResultQueueWorker = !bool.TryParse(configuration["HostedWorkers:EnableResultQueueWorker"], out var enableResultQueueWorker)
                    || enableResultQueueWorker
            },
            OutboxWorker = new OutboxWorkerOptions
            {
                MaxMessagesPerCycle = int.TryParse(configuration["Outbox:MaxMessagesPerCycle"], out var outboxMaxMessages)
                    ? outboxMaxMessages
                    : 25,
                IdleDelay = TimeSpan.FromSeconds(
                    double.TryParse(configuration["Outbox:IdleDelaySeconds"], out var outboxIdleDelay)
                        ? outboxIdleDelay
                        : 1),
                ErrorDelay = TimeSpan.FromSeconds(
                    double.TryParse(configuration["Outbox:ErrorDelaySeconds"], out var outboxErrorDelay)
                        ? outboxErrorDelay
                        : 2)
            },
            ResultQueueWorker = new ResultQueueWorkerOptions
            {
                IdleDelay = TimeSpan.FromSeconds(
                    double.TryParse(configuration["ResultQueue:IdleDelaySeconds"], out var resultIdleDelay)
                        ? resultIdleDelay
                        : 1),
                ErrorDelay = TimeSpan.FromSeconds(
                    double.TryParse(configuration["ResultQueue:ErrorDelaySeconds"], out var resultErrorDelay)
                        ? resultErrorDelay
                        : 2)
            }
        };
    }
}
