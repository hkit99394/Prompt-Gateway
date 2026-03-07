using ControlPlane.Api;
using ControlPlane.Api.Auth;
using ControlPlane.Aws;
using ControlPlane.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IIdGenerator, GuidIdGenerator>();
builder.Services.AddSingleton<ICanonicalResponseAssembler, SimpleResponseAssembler>();

var routingOptions = new RoutingPolicyOptions
{
    Provider = builder.Configuration["Routing:Provider"] ?? string.Empty,
    Model = builder.Configuration["Routing:Model"] ?? string.Empty,
    PolicyVersion = builder.Configuration["Routing:PolicyVersion"] ?? "static",
    FallbackProviders = builder.Configuration.GetSection("Routing:FallbackProviders")
        .GetChildren()
        .Select(child => child.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!)
        .ToArray()
};

builder.Services.AddSingleton<IRoutingPolicy>(_ => new StaticRoutingPolicy(routingOptions));

var retryOptions = new RetryPlannerOptions
{
    MaxAttempts = int.TryParse(builder.Configuration["Retry:MaxAttempts"], out var attempts)
        ? attempts
        : 3
};

builder.Services.AddSingleton<IRetryPlanner>(_ => new FallbackRetryPlanner(retryOptions));

var awsQueueOptions = new AwsQueueOptions
{
    DispatchQueueUrl = builder.Configuration["AwsQueue:DispatchQueueUrl"] ?? string.Empty
};

var dynamoOptions = new DynamoDbOptions
{
    TableName = builder.Configuration["AwsStorage:TableName"] ?? string.Empty
};

builder.Services.AddControlPlaneAws(awsQueueOptions, dynamoOptions);

var outboxOptions = new OutboxWorkerOptions
{
    IdleDelay = TimeSpan.FromSeconds(
        double.TryParse(builder.Configuration["Outbox:IdleDelaySeconds"], out var idleDelay) ? idleDelay : 1),
    ErrorDelay = TimeSpan.FromSeconds(
        double.TryParse(builder.Configuration["Outbox:ErrorDelaySeconds"], out var errorDelay) ? errorDelay : 2)
};

builder.Services.AddSingleton(outboxOptions);
builder.Services.AddSingleton<JobOrchestrator>();
builder.Services.AddSingleton<DispatchOutboxProcessor>();
builder.Services.AddHostedService<OutboxWorker>();

if (string.IsNullOrWhiteSpace(builder.Configuration["ApiSecurity:ApiKey"]))
{
    throw new InvalidOperationException("ApiSecurity:ApiKey must be configured.");
}

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program
{
}
