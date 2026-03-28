using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using ControlPlane.Aws;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ControlPlane.Api.Health;

public sealed class AwsDependenciesHealthCheck : IHealthCheck
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly IAmazonSQS _sqs;
    private readonly AwsQueueOptions _queueOptions;
    private readonly DynamoDbOptions _dynamoOptions;
    private readonly ILogger<AwsDependenciesHealthCheck> _logger;

    public AwsDependenciesHealthCheck(
        IAmazonDynamoDB dynamoDb,
        IAmazonSQS sqs,
        AwsQueueOptions queueOptions,
        DynamoDbOptions dynamoOptions,
        ILogger<AwsDependenciesHealthCheck> logger)
    {
        _dynamoDb = dynamoDb;
        _sqs = sqs;
        _queueOptions = queueOptions;
        _dynamoOptions = dynamoOptions;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_dynamoOptions.TableName))
        {
            return HealthCheckResult.Unhealthy("AwsStorage:TableName is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_queueOptions.DispatchQueueUrl))
        {
            return HealthCheckResult.Unhealthy("AwsQueue:DispatchQueueUrl is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_queueOptions.ResultQueueUrl))
        {
            return HealthCheckResult.Unhealthy("AwsQueue:ResultQueueUrl is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_dynamoOptions.JobListIndexName))
        {
            return HealthCheckResult.Unhealthy("AwsStorage:JobListIndexName is not configured.");
        }

        try
        {
            var describeTableResponse = await _dynamoDb.DescribeTableAsync(
                new DescribeTableRequest { TableName = _dynamoOptions.TableName },
                cancellationToken);

            var indexExists = (describeTableResponse.Table?.GlobalSecondaryIndexes ?? new List<GlobalSecondaryIndexDescription>())
                .Any(index => string.Equals(index.IndexName, _dynamoOptions.JobListIndexName, StringComparison.Ordinal));

            if (!indexExists)
            {
                return HealthCheckResult.Unhealthy(
                    $"AWS dependency check failed. DynamoDB GSI '{_dynamoOptions.JobListIndexName}' was not found on table '{_dynamoOptions.TableName}'.");
            }

            await _sqs.GetQueueAttributesAsync(
                new GetQueueAttributesRequest
                {
                    QueueUrl = _queueOptions.DispatchQueueUrl,
                    AttributeNames = new List<string> { QueueAttributeName.QueueArn }
                },
                cancellationToken);

            await _sqs.GetQueueAttributesAsync(
                new GetQueueAttributesRequest
                {
                    QueueUrl = _queueOptions.ResultQueueUrl,
                    AttributeNames = new List<string> { QueueAttributeName.QueueArn }
                },
                cancellationToken);

            return HealthCheckResult.Healthy("AWS dependencies and DynamoDB index are reachable.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AWS dependency check failed. TableName={TableName}, JobListIndexName={JobListIndexName}, DispatchQueueUrl={DispatchUrl}, ResultQueueUrl={ResultUrl}",
                _dynamoOptions.TableName, _dynamoOptions.JobListIndexName, _queueOptions.DispatchQueueUrl, _queueOptions.ResultQueueUrl);
            return HealthCheckResult.Unhealthy("AWS dependency check failed.", ex);
        }
    }
}
