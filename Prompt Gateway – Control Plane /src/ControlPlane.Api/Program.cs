using System.Text.Json.Serialization;
using ControlPlane.Api;
using ControlPlane.Api.Auth;
using ControlPlane.Api.Health;
using ControlPlane.Aws;
using ControlPlane.Core;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddControlPlaneRuntime(builder.Configuration);

// The HTTP control plane is still ECS-hosted today, but the next migration phases target a
// Lambda-hosted HTTP edge after the queue-processing path is fully proven.
var outboxOptions = new OutboxWorkerOptions
{
    MaxMessagesPerCycle = int.TryParse(builder.Configuration["Outbox:MaxMessagesPerCycle"], out var outboxMaxMessages)
        ? outboxMaxMessages
        : 25,
    IdleDelay = TimeSpan.FromSeconds(
        double.TryParse(builder.Configuration["Outbox:IdleDelaySeconds"], out var idleDelay) ? idleDelay : 1),
    ErrorDelay = TimeSpan.FromSeconds(
        double.TryParse(builder.Configuration["Outbox:ErrorDelaySeconds"], out var errorDelay) ? errorDelay : 2)
};

var hostedWorkerOptions = new HostedWorkerOptions
{
    EnablePostAcceptResumeWorker = bool.TryParse(builder.Configuration["HostedWorkers:EnablePostAcceptResumeWorker"], out var enablePostAcceptResumeWorker)
        ? enablePostAcceptResumeWorker
        : true,
    EnableOutboxWorker = bool.TryParse(builder.Configuration["HostedWorkers:EnableOutboxWorker"], out var enableOutboxWorker)
        ? enableOutboxWorker
        : true,
    EnableResultQueueWorker = bool.TryParse(builder.Configuration["HostedWorkers:EnableResultQueueWorker"], out var enableResultQueueWorker)
        ? enableResultQueueWorker
        : true
};

builder.Services.AddSingleton(hostedWorkerOptions);
builder.Services.AddSingleton<PostAcceptResumeQueue>();
builder.Services.AddSingleton<IPostAcceptResumeScheduler>(provider => provider.GetRequiredService<PostAcceptResumeQueue>());
if (hostedWorkerOptions.EnablePostAcceptResumeWorker)
{
    builder.Services.AddHostedService<PostAcceptResumeWorker>();
}

builder.Services.AddSingleton(outboxOptions);
if (hostedWorkerOptions.EnableOutboxWorker)
{
    builder.Services.AddHostedService<OutboxWorker>();
}

var resultQueueOptions = new ResultQueueWorkerOptions
{
    IdleDelay = TimeSpan.FromSeconds(
        double.TryParse(builder.Configuration["ResultQueue:IdleDelaySeconds"], out var rqIdle) ? rqIdle : 1),
    ErrorDelay = TimeSpan.FromSeconds(
        double.TryParse(builder.Configuration["ResultQueue:ErrorDelaySeconds"], out var rqErr) ? rqErr : 2)
};
builder.Services.AddSingleton(resultQueueOptions);
builder.Services.AddSingleton<ResultQueueProcessor>();
if (hostedWorkerOptions.EnableResultQueueWorker)
{
    builder.Services.AddHostedService<ResultQueueWorker>();
}
builder.Services.AddHealthChecks()
    .AddCheck("live", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddCheck<AwsDependenciesHealthCheck>("aws_dependencies", tags: new[] { "ready" });

if (ApiKeyConfiguration.GetConfiguredApiKeys(builder.Configuration).Count == 0)
{
    throw new InvalidOperationException("At least one API key must be configured in ApiSecurity:ApiKeys or ApiSecurity:ApiKey.");
}

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            entries = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                exception = e.Value.Exception?.Message
            })
        };
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(result));
    }
});

app.Run();

public partial class Program
{
}
