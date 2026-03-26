using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Provider.Worker;
using Provider.Worker.Aws;
using Provider.Worker.Options;
using Provider.Worker.Services;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Provider.Worker.Lambda;

public sealed class ProviderDispatchFunction
{
    private readonly IHost? _host;
    private readonly IProviderMessageProcessor _messageProcessor;
    private readonly ILogger<ProviderDispatchFunction> _logger;

    public ProviderDispatchFunction()
        : this(BuildHost())
    {
    }

    public ProviderDispatchFunction(
        IProviderMessageProcessor messageProcessor,
        ILogger<ProviderDispatchFunction> logger)
    {
        _messageProcessor = messageProcessor;
        _logger = logger;
    }

    private ProviderDispatchFunction(IHost host)
    {
        _host = host;
        _messageProcessor = host.Services.GetRequiredService<IProviderMessageProcessor>();
        _logger = host.Services.GetRequiredService<ILogger<ProviderDispatchFunction>>();

        // Force options materialization so misconfiguration fails during cold start.
        _ = host.Services.GetRequiredService<IOptions<ProviderWorkerOptions>>().Value;
    }

    public Task<SqsBatchResponse> FunctionHandler(SqsEvent sqsEvent)
    {
        return FunctionHandlerAsync(sqsEvent, CancellationToken.None);
    }

    public async Task<SqsBatchResponse> FunctionHandlerAsync(
        SqsEvent sqsEvent,
        CancellationToken cancellationToken)
    {
        if (sqsEvent.Records is null || sqsEvent.Records.Count == 0)
        {
            _logger.LogInformation("Provider Lambda invoked with no SQS records.");
            return new SqsBatchResponse();
        }

        var response = new SqsBatchResponse();

        foreach (var record in sqsEvent.Records)
        {
            try
            {
                var result = await _messageProcessor.ProcessAsync(new QueueMessage
                {
                    Body = record.Body,
                    ReceiptHandle = record.ReceiptHandle
                }, cancellationToken);

                if (!result.ShouldAcknowledge)
                {
                    response.BatchItemFailures.Add(CreateBatchFailure(record.MessageId));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider Lambda processing failed for SQS message {MessageId}.", record.MessageId);
                response.BatchItemFailures.Add(CreateBatchFailure(record.MessageId));
            }
        }

        return response;
    }

    private static SqsBatchResponse.BatchItemFailure CreateBatchFailure(string? messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            throw new InvalidOperationException("SQS record is missing messageId.");
        }

        return new SqsBatchResponse.BatchItemFailure
        {
            ItemIdentifier = messageId
        };
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        LambdaSecretConfiguration.ApplyAsync(builder.Configuration, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        builder.Services.AddOptions<ProviderWorkerOptions>()
            .Bind(builder.Configuration.GetSection(ProviderWorkerOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.TryValidate(out _), "Invalid ProviderWorker configuration.")
            .ValidateOnStart();

        builder.Services.AddProviderWorkerAws();
        builder.Services.AddProviderWorkerCore();

        return builder.Build();
    }
}
