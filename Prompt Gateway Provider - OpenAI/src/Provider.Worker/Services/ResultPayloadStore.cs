using System.Text;
using Microsoft.Extensions.Options;
using Provider.Worker.Models;
using Provider.Worker.Options;

namespace Provider.Worker.Services;

public interface IResultPayloadStore
{
    Task<string?> StoreIfLargeAsync(
        CanonicalJobRequest job,
        string payload,
        CancellationToken cancellationToken);

    Task<string?> StoreAsync(
        CanonicalJobRequest job,
        string payload,
        string fileName,
        CancellationToken cancellationToken);
}

public class ResultPayloadStore(IObjectStore objectStore, IOptions<ProviderWorkerOptions> options) : IResultPayloadStore
{
    private readonly IObjectStore _objectStore = objectStore;
    private readonly ProviderWorkerOptions _options = options.Value;

    public async Task<string?> StoreIfLargeAsync(
        CanonicalJobRequest job,
        string payload,
        CancellationToken cancellationToken)
    {
        var payloadBytes = Encoding.UTF8.GetByteCount(payload);
        if (payloadBytes <= _options.LargePayloadThresholdBytes)
        {
            return null;
        }

        var bucket = string.IsNullOrWhiteSpace(_options.ResultBucket)
            ? job.PromptS3Bucket ?? _options.PromptBucket
            : _options.ResultBucket;

        if (string.IsNullOrWhiteSpace(bucket))
        {
            return null;
        }

        var key = $"{_options.ResultPrefix}{job.JobId}/{job.AttemptId}/payload.json";

        return await _objectStore.PutObjectTextAsync(
            bucket,
            key,
            payload,
            "application/json",
            cancellationToken);
    }

    public Task<string?> StoreAsync(
        CanonicalJobRequest job,
        string payload,
        string fileName,
        CancellationToken cancellationToken)
    {
        return StoreAsyncInternal(job, payload, fileName, cancellationToken);
    }

    private async Task<string?> StoreAsyncInternal(
        CanonicalJobRequest job,
        string payload,
        string fileName,
        CancellationToken cancellationToken)
    {
        var bucket = string.IsNullOrWhiteSpace(_options.ResultBucket)
            ? job.PromptS3Bucket ?? _options.PromptBucket
            : _options.ResultBucket;

        if (string.IsNullOrWhiteSpace(bucket))
        {
            return null;
        }

        var key = $"{_options.ResultPrefix}{job.JobId}/{job.AttemptId}/{fileName}";

        return await _objectStore.PutObjectTextAsync(
            bucket,
            key,
            payload,
            "application/json",
            cancellationToken);
    }
}
