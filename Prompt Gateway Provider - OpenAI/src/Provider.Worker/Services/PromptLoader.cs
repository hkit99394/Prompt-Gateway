using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Provider.Worker.Models;
using Provider.Worker.Options;

namespace Provider.Worker.Services;

public interface IPromptLoader
{
    Task<string> LoadPromptAsync(CanonicalJobRequest job, CancellationToken cancellationToken);
}

public class PromptLoader(IAmazonS3 s3, IOptions<ProviderWorkerOptions> options) : IPromptLoader
{
    private readonly IAmazonS3 _s3 = s3;
    private readonly ProviderWorkerOptions _options = options.Value;

    public async Task<string> LoadPromptAsync(CanonicalJobRequest job, CancellationToken cancellationToken)
    {
        var bucket = job.PromptS3Bucket ?? _options.PromptBucket;
        if (string.IsNullOrWhiteSpace(bucket))
        {
            throw new InvalidOperationException("Prompt bucket not configured.");
        }

        if (string.IsNullOrWhiteSpace(job.PromptS3Key))
        {
            throw new InvalidOperationException("Prompt S3 key missing.");
        }

        var request = new GetObjectRequest
        {
            BucketName = bucket,
            Key = job.PromptS3Key
        };

        using var response = await _s3.GetObjectAsync(request, cancellationToken);
        using var stream = response.ResponseStream;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
