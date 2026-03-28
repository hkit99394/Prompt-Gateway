using Amazon.Lambda.Core;
using ControlPlane.Aws;
using ControlPlane.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ControlPlane.ResultLambda;

public sealed class ResultQueueFunction
{
    private readonly IHost? _host;
    private readonly IResultMessageProcessor _messageProcessor;
    private readonly ILogger<ResultQueueFunction> _logger;

    public ResultQueueFunction()
        : this(BuildHost())
    {
    }

    public ResultQueueFunction(
        IResultMessageProcessor messageProcessor,
        ILogger<ResultQueueFunction> logger)
    {
        _messageProcessor = messageProcessor;
        _logger = logger;
    }

    private ResultQueueFunction(IHost host)
    {
        _host = host;
        _messageProcessor = host.Services.GetRequiredService<IResultMessageProcessor>();
        _logger = host.Services.GetRequiredService<ILogger<ResultQueueFunction>>();
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
            _logger.LogInformation("Result Lambda invoked with no SQS records.");
            return new SqsBatchResponse();
        }

        var response = new SqsBatchResponse();
        foreach (var record in sqsEvent.Records)
        {
            try
            {
                var result = await _messageProcessor.ProcessAsync(
                    record.Body ?? string.Empty,
                    record.MessageId,
                    cancellationToken);
                if (!result.ShouldAcknowledge)
                {
                    response.BatchItemFailures.Add(CreateBatchFailure(record.MessageId));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Result Lambda processing failed for SQS message {MessageId}.", record.MessageId);
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
        builder.Services.AddControlPlaneRuntime(builder.Configuration);

        return builder.Build();
    }
}
