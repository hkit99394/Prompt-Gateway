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
        var key = job.PromptKey ?? job.PromptS3Key;
        if (string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(job.InputRef))
        {
            if (TryParseS3Uri(job.InputRef!, out var parsedBucket, out var parsedKey))
            {
                requestedBucket ??= parsedBucket;
                key = parsedKey;
            }
            else
            {
                key = job.InputRef;
            }
        }

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

    private static bool TryParseS3Uri(string inputRef, out string bucket, out string key)
    {
        bucket = string.Empty;
        key = string.Empty;

        if (!Uri.TryCreate(inputRef, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, "s3", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        var parsedKey = uri.AbsolutePath.TrimStart('/');
        if (string.IsNullOrWhiteSpace(parsedKey))
        {
            return false;
        }

        bucket = uri.Host;
        key = parsedKey;
        return true;
    }
}
