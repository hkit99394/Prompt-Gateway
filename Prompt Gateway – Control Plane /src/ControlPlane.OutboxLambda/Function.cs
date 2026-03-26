using Amazon.Lambda.Core;
using ControlPlane.Aws;
using ControlPlane.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ControlPlane.OutboxLambda;

public sealed class OutboxDispatchFunction
{
    private readonly IHost? _host;
    private readonly IOutboxDispatchBatchProcessor _batchProcessor;
    private readonly ILogger<OutboxDispatchFunction> _logger;
    private readonly OutboxLambdaOptions _options;

    public OutboxDispatchFunction()
        : this(BuildHost())
    {
    }

    public OutboxDispatchFunction(
        IOutboxDispatchBatchProcessor batchProcessor,
        ILogger<OutboxDispatchFunction> logger,
        OutboxLambdaOptions options)
    {
        _batchProcessor = batchProcessor;
        _logger = logger;
        _options = options;
    }

    private OutboxDispatchFunction(IHost host)
    {
        _host = host;
        _batchProcessor = host.Services.GetRequiredService<IOutboxDispatchBatchProcessor>();
        _logger = host.Services.GetRequiredService<ILogger<OutboxDispatchFunction>>();
        _options = host.Services.GetRequiredService<OutboxLambdaOptions>();
    }

    public Task<OutboxDispatchInvocationResult> FunctionHandler(object? trigger)
    {
        return FunctionHandlerAsync(CancellationToken.None);
    }

    public async Task<OutboxDispatchInvocationResult> FunctionHandlerAsync(CancellationToken cancellationToken)
    {
        var result = await _batchProcessor.ProcessAsync(_options.MaxMessagesPerInvocation, cancellationToken);

        _logger.LogInformation(
            "Outbox Lambda processed {ProcessedCount} messages. ReachedLimit={ReachedLimit}.",
            result.ProcessedCount,
            result.ReachedLimit);

        return new OutboxDispatchInvocationResult
        {
            ProcessedCount = result.ProcessedCount,
            ReachedLimit = result.ReachedLimit
        };
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        var awsQueueOptions = new AwsQueueOptions
        {
            DispatchQueueUrl = builder.Configuration["AwsQueue:DispatchQueueUrl"] ?? string.Empty,
            ResultQueueUrl = builder.Configuration["AwsQueue:ResultQueueUrl"] ?? string.Empty
        };

        var dynamoOptions = new DynamoDbOptions
        {
            TableName = builder.Configuration["AwsStorage:TableName"] ?? string.Empty,
            JobListIndexName = builder.Configuration["AwsStorage:JobListIndexName"] ?? "gsi1",
            DeduplicationTtlDays = int.TryParse(builder.Configuration["AwsStorage:DeduplicationTtlDays"], out var dedupeTtlDays)
                ? dedupeTtlDays
                : 7,
            OutboxTerminalTtlDays = int.TryParse(builder.Configuration["AwsStorage:OutboxTerminalTtlDays"], out var outboxTtlDays)
                ? outboxTtlDays
                : 7,
            EventTtlDays = int.TryParse(builder.Configuration["AwsStorage:EventTtlDays"], out var eventTtlDays)
                ? eventTtlDays
                : 30,
            ResultTtlDays = int.TryParse(builder.Configuration["AwsStorage:ResultTtlDays"], out var resultTtlDays)
                ? resultTtlDays
                : 30
        };

        var options = new OutboxLambdaOptions
        {
            MaxMessagesPerInvocation = int.TryParse(
                builder.Configuration["OutboxLambda:MaxMessagesPerInvocation"],
                out var maxMessages)
                ? maxMessages
                : 25
        };

        builder.Services.AddSingleton(options);
        builder.Services.AddControlPlaneAws(awsQueueOptions, dynamoOptions);
        builder.Services.AddSingleton<DispatchOutboxProcessor>();
        builder.Services.AddSingleton<IOutboxDispatchBatchProcessor, OutboxDispatchBatchProcessor>();

        return builder.Build();
    }
}
