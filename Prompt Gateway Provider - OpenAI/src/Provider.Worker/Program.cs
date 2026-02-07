using Amazon.DynamoDBv2;
using Amazon.S3;
using Amazon.SQS;
using Microsoft.Extensions.Options;
using Provider.Worker;
using Provider.Worker.Options;
using Provider.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<ProviderWorkerOptions>(
    builder.Configuration.GetSection(ProviderWorkerOptions.SectionName));

builder.Services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient());
builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());

builder.Services.AddSingleton<IDedupeStore, DedupeStore>();
builder.Services.AddSingleton<IPromptLoader, PromptLoader>();
builder.Services.AddSingleton<IResultPayloadStore, ResultPayloadStore>();
builder.Services.AddSingleton<IResultPublisher, ResultPublisher>();
builder.Services.AddSingleton<ISqsClient, SqsClient>();
builder.Services.AddHttpClient<OpenAiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<ProviderWorkerOptions>>().Value;
    var baseUrl = options.OpenAi.BaseUrl.TrimEnd('/') + "/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.OpenAi.TimeoutSeconds);
});
builder.Services.AddSingleton<IOpenAiClient>(sp => sp.GetRequiredService<OpenAiClient>());

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
