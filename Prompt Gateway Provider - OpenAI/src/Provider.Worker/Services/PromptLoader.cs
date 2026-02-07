using Microsoft.Extensions.Options;
using Provider.Worker.Models;
using Provider.Worker.Options;

namespace Provider.Worker.Services;

public interface IPromptTemplateStore
{
    Task<string> GetTemplateAsync(CanonicalJobRequest job, CancellationToken cancellationToken);
}

public class PromptTemplateStore(IObjectStore objectStore, IOptions<ProviderWorkerOptions> options)
    : IPromptTemplateStore
{
    private readonly IObjectStore _objectStore = objectStore;
    private readonly ProviderWorkerOptions _options = options.Value;

    public async Task<string> GetTemplateAsync(CanonicalJobRequest job, CancellationToken cancellationToken)
    {
        var bucket = job.PromptBucket ?? job.PromptS3Bucket ?? _options.PromptBucket;
        if (string.IsNullOrWhiteSpace(bucket))
        {
            throw new InvalidOperationException("Prompt bucket not configured.");
        }

        var key = job.PromptKey ?? job.PromptS3Key;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Prompt key missing.");
        }

        return await _objectStore.GetObjectTextAsync(bucket, key, cancellationToken);
    }
}
