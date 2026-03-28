using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Provider.Worker.Options;

namespace Provider.Worker.Aws;

public static class ProviderWorkerRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddProviderWorkerRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ProviderWorkerOptions>()
            .Bind(configuration.GetSection(ProviderWorkerOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.TryValidate(out _), "Invalid ProviderWorker configuration.")
            .ValidateOnStart();

        services.AddProviderWorkerAws();
        services.AddProviderWorkerCore();

        return services;
    }
}
