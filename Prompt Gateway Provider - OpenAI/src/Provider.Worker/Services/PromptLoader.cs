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
        var requestedBucket = job.PromptBucket ?? job.PromptS3Bucket;
        if (!string.IsNullOrWhiteSpace(requestedBucket) &&
            (string.IsNullOrWhiteSpace(_options.PromptBucket) ||
             !string.Equals(requestedBucket, _options.PromptBucket, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Prompt bucket override is not allowed.");
        }

        var bucket = requestedBucket ?? _options.PromptBucket;
        if (string.IsNullOrWhiteSpace(bucket))
        {
            throw new InvalidOperationException("Prompt bucket not configured.");
        }

        var key = job.PromptKey ?? job.PromptS3Key;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Prompt key missing.");
        }

        ValidateBucketName(bucket);
        ValidateObjectKey(key);

        return await _objectStore.GetObjectTextAsync(bucket, key, cancellationToken);
    }

    private static void ValidateBucketName(string bucket)
    {
        if (bucket.Contains('/') || bucket.Contains('\\'))
        {
            throw new InvalidOperationException("Invalid prompt bucket name.");
        }
    }

    private static void ValidateObjectKey(string key)
    {
        if (key.Contains("..", StringComparison.Ordinal) ||
            key.StartsWith("/", StringComparison.Ordinal) ||
            key.StartsWith("\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid prompt key.");
        }
    }
}
