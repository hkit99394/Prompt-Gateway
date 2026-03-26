using Microsoft.Extensions.DependencyInjection;
using Provider.Worker.Services;

namespace Provider.Worker;

public static class ProviderWorkerServiceCollectionExtensions
{
    public static IServiceCollection AddProviderWorkerCore(this IServiceCollection services)
    {
        services.AddSingleton<IPromptTemplateStore, PromptTemplateStore>();
        services.AddSingleton<IPromptBuilder, PromptBuilder>();
        services.AddSingleton<IResultPayloadStore, ResultPayloadStore>();
        services.AddSingleton<OpenAiClient>();
        services.AddSingleton<IOpenAiClient>(sp => sp.GetRequiredService<OpenAiClient>());
        services.AddSingleton<IProviderMessageProcessor, ProviderMessageProcessor>();

        return services;
    }
}
