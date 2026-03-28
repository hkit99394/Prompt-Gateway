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

        var options = new OutboxLambdaOptions
        {
            MaxMessagesPerInvocation = int.TryParse(
                builder.Configuration["OutboxLambda:MaxMessagesPerInvocation"],
                out var maxMessages)
                ? maxMessages
                : 25
        };

        builder.Services.AddSingleton(options);
        builder.Services.AddControlPlaneRuntime(builder.Configuration);

        return builder.Build();
    }
}
