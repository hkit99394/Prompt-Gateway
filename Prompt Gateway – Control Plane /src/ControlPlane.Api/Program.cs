using ControlPlane.Api;
using ControlPlane.Aws;
using ControlPlane.Core;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

app.MapPost("/jobs", async (CanonicalJobRequest request, JobOrchestrator orchestrator, CancellationToken ct) =>
{
    var handle = await orchestrator.AcceptAsync(request, ct);
    var routing = await orchestrator.RouteAsync(handle.JobId, ct);
    var dispatch = await orchestrator.DispatchAsync(handle.JobId, handle.AttemptId, ct);

    return Results.Ok(new
    {
        handle.JobId,
        handle.AttemptId,
        handle.TraceId,
        routing,
        dispatch.IdempotencyKey
    });
});

app.MapGet("/jobs/{jobId}", async (string jobId, JobOrchestrator orchestrator, CancellationToken ct) =>
{
    var job = await orchestrator.GetJobAsync(jobId, ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.MapGet("/jobs/{jobId}/result", async (string jobId, JobOrchestrator orchestrator, CancellationToken ct) =>
{
    var result = await orchestrator.GetFinalResultAsync(jobId, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapGet("/jobs/{jobId}/events", async (string jobId, JobOrchestrator orchestrator, CancellationToken ct) =>
{
    var events = await orchestrator.GetEventsAsync(jobId, ct);
    return Results.Ok(events);
});

app.Run();
