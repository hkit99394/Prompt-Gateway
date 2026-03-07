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

    public AwsDependenciesHealthCheck(
        IAmazonDynamoDB dynamoDb,
        IAmazonSQS sqs,
        AwsQueueOptions queueOptions,
        DynamoDbOptions dynamoOptions)
    {
        _dynamoDb = dynamoDb;
        _sqs = sqs;
        _queueOptions = queueOptions;
        _dynamoOptions = dynamoOptions;
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

        try
        {
            await _dynamoDb.DescribeTableAsync(
                new DescribeTableRequest { TableName = _dynamoOptions.TableName },
                cancellationToken);

            await _sqs.GetQueueAttributesAsync(
                new GetQueueAttributesRequest
                {
                    QueueUrl = _queueOptions.DispatchQueueUrl,
                    AttributeNames = new List<string> { QueueAttributeName.QueueArn }
                },
                cancellationToken);

            return HealthCheckResult.Healthy("AWS dependencies are reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("AWS dependency check failed.", ex);
        }
    }
}
