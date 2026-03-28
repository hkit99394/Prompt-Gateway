using Amazon.Lambda.AspNetCoreServer.Hosting;
using ControlPlane.Api;
using ControlPlane.Api.Lambda;

var builder = WebApplication.CreateBuilder(args);

await LambdaApiKeyConfiguration.ApplyAsync(builder.Configuration, CancellationToken.None);

builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
builder.Services.AddControlPlaneHttpApi(
    builder.Configuration,
    hostOptions =>
    {
        hostOptions.EnableSwagger = false;
        hostOptions.HostedWorkers = new HostedWorkerOptions
        {
            EnablePostAcceptResumeWorker = false,
            EnableOutboxWorker = false,
            EnableResultQueueWorker = false
        };
    });

var app = builder.Build();
app.MapControlPlaneHttpApi();
app.Run();

public partial class Program;
