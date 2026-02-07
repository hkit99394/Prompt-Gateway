using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
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

public class ResultPayloadStore(IAmazonS3 s3, IOptions<ProviderWorkerOptions> options) : IResultPayloadStore
{
    private readonly IAmazonS3 _s3 = s3;
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

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            ContentBody = payload,
            ContentType = "application/json"
        };

        await _s3.PutObjectAsync(request, cancellationToken);
        return $"s3://{bucket}/{key}";
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

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            ContentBody = payload,
            ContentType = "application/json"
        };

        await _s3.PutObjectAsync(request, cancellationToken);
        return $"s3://{bucket}/{key}";
    }
}
