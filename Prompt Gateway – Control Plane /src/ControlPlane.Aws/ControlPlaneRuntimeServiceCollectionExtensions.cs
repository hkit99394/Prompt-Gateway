using ControlPlane.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Aws;

public static class ControlPlaneRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddControlPlaneRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var routingOptions = CreateRoutingPolicyOptions(configuration);
        var retryOptions = CreateRetryPlannerOptions(configuration);
        var awsQueueOptions = CreateAwsQueueOptions(configuration);
        var dynamoOptions = CreateDynamoDbOptions(configuration);

        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        services.AddSingleton<ICanonicalResponseAssembler, SimpleResponseAssembler>();
        services.AddSingleton<IRoutingPolicy>(_ => new StaticRoutingPolicy(routingOptions));
        services.AddSingleton<IRetryPlanner>(_ => new FallbackRetryPlanner(retryOptions));
        services.AddControlPlaneAws(awsQueueOptions, dynamoOptions);
        services.AddSingleton<JobOrchestrator>();
        services.AddSingleton<IResultIngestionOrchestrator, JobOrchestratorResultIngester>();
        services.AddSingleton<IResultMessageProcessor, ResultMessageProcessor>();
        services.AddSingleton<DispatchOutboxProcessor>();
        services.AddSingleton<IOutboxDispatchBatchProcessor, OutboxDispatchBatchProcessor>();

        return services;
    }

    private static RoutingPolicyOptions CreateRoutingPolicyOptions(IConfiguration configuration)
    {
        return new RoutingPolicyOptions
        {
            Provider = configuration["Routing:Provider"] ?? string.Empty,
            Model = configuration["Routing:Model"] ?? string.Empty,
            PolicyVersion = configuration["Routing:PolicyVersion"] ?? "static",
            FallbackProviders = configuration.GetSection("Routing:FallbackProviders")
                .GetChildren()
                .Select(child => child.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray()
        };
    }

    private static RetryPlannerOptions CreateRetryPlannerOptions(IConfiguration configuration)
    {
        return new RetryPlannerOptions
        {
            MaxAttempts = int.TryParse(configuration["Retry:MaxAttempts"], out var attempts)
                ? attempts
                : 3
        };
    }

    private static AwsQueueOptions CreateAwsQueueOptions(IConfiguration configuration)
    {
        return new AwsQueueOptions
        {
            DispatchQueueUrl = configuration["AwsQueue:DispatchQueueUrl"] ?? string.Empty,
            ResultQueueUrl = configuration["AwsQueue:ResultQueueUrl"] ?? string.Empty
        };
    }

    private static DynamoDbOptions CreateDynamoDbOptions(IConfiguration configuration)
    {
        return new DynamoDbOptions
        {
            TableName = configuration["AwsStorage:TableName"] ?? string.Empty,
            JobListIndexName = configuration["AwsStorage:JobListIndexName"] ?? "gsi1",
            DeduplicationTtlDays = int.TryParse(configuration["AwsStorage:DeduplicationTtlDays"], out var dedupeTtlDays)
                ? dedupeTtlDays
                : 7,
            OutboxTerminalTtlDays = int.TryParse(configuration["AwsStorage:OutboxTerminalTtlDays"], out var outboxTtlDays)
                ? outboxTtlDays
                : 7,
            EventTtlDays = int.TryParse(configuration["AwsStorage:EventTtlDays"], out var eventTtlDays)
                ? eventTtlDays
                : 30,
            ResultTtlDays = int.TryParse(configuration["AwsStorage:ResultTtlDays"], out var resultTtlDays)
                ? resultTtlDays
                : 30
        };
    }
}
