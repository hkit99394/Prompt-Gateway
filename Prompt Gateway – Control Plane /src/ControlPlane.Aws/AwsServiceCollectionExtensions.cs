using Amazon.DynamoDBv2;
using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using ControlPlane.Core;

namespace ControlPlane.Aws;

public static class AwsServiceCollectionExtensions
{
    public static IServiceCollection AddControlPlaneAws(
        this IServiceCollection services,
        AwsQueueOptions queueOptions,
        DynamoDbOptions dynamoOptions)
    {
        services.AddSingleton(queueOptions);
        services.AddSingleton(dynamoOptions);
        services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient());
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
        services.AddSingleton<ControlPlane.Core.IDispatchQueue, SqsDispatchQueue>();
        services.AddSingleton<IJobStore, DynamoDbJobStore>();
        services.AddSingleton<IJobEventStore, DynamoDbJobEventStore>();
        services.AddSingleton<IOutboxStore, DynamoDbOutboxStore>();
        services.AddSingleton<IResultStore, DynamoDbResultStore>();
        services.AddSingleton<IDeduplicationStore, DynamoDbDeduplicationStore>();
        return services;
    }
}
