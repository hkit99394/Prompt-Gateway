using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Provider.Worker.Services;

namespace Provider.Worker.Aws;

public class S3ObjectStore(IAmazonS3 s3) : IObjectStore
{
    private readonly IAmazonS3 _s3 = s3;

    public async Task<string> GetObjectTextAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        var request = new GetObjectRequest
        {
            BucketName = bucket,
            Key = key
        };

        using var response = await _s3.GetObjectAsync(request, cancellationToken);
        using var stream = response.ResponseStream;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public async Task<string> PutObjectTextAsync(
        string bucket,
        string key,
        string content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            ContentBody = content,
            ContentType = contentType
        };

        await _s3.PutObjectAsync(request, cancellationToken);
        return $"s3://{bucket}/{key}";
    }
}
