using Microsoft.Extensions.Options;
using Provider.Worker;
using Provider.Worker.Aws;
using Provider.Worker.Options;
using Provider.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<ProviderWorkerOptions>()
    .Bind(builder.Configuration.GetSection(ProviderWorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => options.TryValidate(out _), "Invalid ProviderWorker configuration.")
    .ValidateOnStart();

builder.Services.AddProviderWorkerAws();
builder.Services.AddProviderWorkerCore();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
