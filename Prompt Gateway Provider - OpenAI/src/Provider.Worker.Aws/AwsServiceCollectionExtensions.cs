using Amazon.DynamoDBv2;
using Amazon.S3;
using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using Provider.Worker.Services;

namespace Provider.Worker.Aws;

public static class AwsServiceCollectionExtensions
{
    public static IServiceCollection AddProviderWorkerAws(this IServiceCollection services)
    {
        services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient());
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
        services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());

        services.AddSingleton<IQueueClient, SqsQueueClient>();
        services.AddSingleton<IObjectStore, S3ObjectStore>();
        services.AddSingleton<IDedupeStore, DedupeStore>();
        services.AddSingleton<IResultPublisher, SqsResultPublisher>();

        return services;
    }
}
