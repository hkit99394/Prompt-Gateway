using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using NSubstitute;
using Provider.Worker.Aws;

namespace Provider.Worker.Tests;

public class S3ObjectStoreTests
{
    [Test]
    public async Task GetObjectTextAsync_RetriesOnTransientError()
    {
        var s3 = Substitute.For<IAmazonS3>();
        var store = new S3ObjectStore(s3);

        var transient = new AmazonServiceException("transient")
        {
            StatusCode = HttpStatusCode.ServiceUnavailable
        };

        var response = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes("ok"))
        };

        s3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<GetObjectResponse>(transient),
                _ => Task.FromResult(response));

        var result = await store.GetObjectTextAsync("bucket", "key", CancellationToken.None);

        Assert.That(result, Is.EqualTo("ok"));
        await s3.Received(2).GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void GetObjectTextAsync_DoesNotRetryOnNonTransientError()
    {
        var s3 = Substitute.For<IAmazonS3>();
        var store = new S3ObjectStore(s3);

        var nonTransient = new AmazonServiceException("not-found")
        {
            StatusCode = HttpStatusCode.NotFound
        };

        s3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<GetObjectResponse>(nonTransient));

        Assert.ThrowsAsync<AmazonServiceException>(async () =>
            await store.GetObjectTextAsync("bucket", "key", CancellationToken.None));
        s3.Received(1).GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>());
    }
}
