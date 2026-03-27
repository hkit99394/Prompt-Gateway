using Provider.Worker;
using Provider.Worker.Aws;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddProviderWorkerRuntime(builder.Configuration);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
