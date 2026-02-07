using Microsoft.Extensions.Options;
using Provider.Worker.Options;

namespace Provider.Worker.Tests;

internal static class TestOptions
{
    public static IOptions<ProviderWorkerOptions> Create(ProviderWorkerOptions? options = null)
    {
        return Microsoft.Extensions.Options.Options.Create(options ?? new ProviderWorkerOptions());
    }
}
