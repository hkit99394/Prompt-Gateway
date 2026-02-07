using System.Net;
using System.Text;
using Amazon.Runtime;
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

        var attempt = 0;
        var maxAttempts = 3;
        while (true)
        {
            attempt++;
            try
            {
                using var response = await _s3.GetObjectAsync(request, cancellationToken);
                using var stream = response.ResponseStream;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return await reader.ReadToEndAsync(cancellationToken);
            }
            catch (Exception ex) when (attempt < maxAttempts &&
                                       ShouldRetry(ex) &&
                                       !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(BackoffDelay(attempt), cancellationToken);
            }
        }
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

        var attempt = 0;
        var maxAttempts = 3;
        while (true)
        {
            attempt++;
            try
            {
                await _s3.PutObjectAsync(request, cancellationToken);
                return $"s3://{bucket}/{key}";
            }
            catch (Exception ex) when (attempt < maxAttempts &&
                                       ShouldRetry(ex) &&
                                       !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(BackoffDelay(attempt), cancellationToken);
            }
        }
    }

    private static bool ShouldRetry(Exception ex)
    {
        if (ex is AmazonServiceException serviceException)
        {
            return serviceException.StatusCode == HttpStatusCode.TooManyRequests
                   || serviceException.StatusCode == HttpStatusCode.RequestTimeout
                   || serviceException.StatusCode == HttpStatusCode.BadGateway
                   || serviceException.StatusCode == HttpStatusCode.ServiceUnavailable
                   || serviceException.StatusCode == HttpStatusCode.GatewayTimeout
                   || serviceException.StatusCode == HttpStatusCode.InternalServerError;
        }

        return false;
    }

    private static TimeSpan BackoffDelay(int attempt)
    {
        var jitter = Random.Shared.NextDouble() * 0.3 + 0.85;
        var seconds = Math.Pow(2, attempt) * jitter;
        return TimeSpan.FromSeconds(Math.Min(10, seconds));
    }
}
