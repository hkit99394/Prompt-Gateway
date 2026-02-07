using Microsoft.Extensions.Options;
using Provider.Worker;
using Provider.Worker.Aws;
using Provider.Worker.Options;
using Provider.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<ProviderWorkerOptions>(
    builder.Configuration.GetSection(ProviderWorkerOptions.SectionName));

builder.Services.AddProviderWorkerAws();
builder.Services.AddSingleton<IPromptTemplateStore, PromptTemplateStore>();
builder.Services.AddSingleton<IPromptBuilder, PromptBuilder>();
builder.Services.AddSingleton<IResultPayloadStore, ResultPayloadStore>();
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
