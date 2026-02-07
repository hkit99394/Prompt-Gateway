using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using NSubstitute;
using Provider.Worker.Models;
using Provider.Worker.Options;
using Provider.Worker.Services;

namespace Provider.Worker.Tests;

public class PromptLoaderTests
{
    [Test]
    public void LoadPromptAsync_ThrowsWhenBucketMissing()
    {
        var s3 = Substitute.For<IAmazonS3>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            PromptBucket = string.Empty
        });
        var loader = new PromptLoader(s3, options);

        var job = new CanonicalJobRequest
        {
            PromptS3Key = "prompts/job.txt"
        };

        Assert.That(
            async () => await loader.LoadPromptAsync(job, CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Prompt bucket not configured."));
    }

    [Test]
    public void LoadPromptAsync_ThrowsWhenKeyMissing()
    {
        var s3 = Substitute.For<IAmazonS3>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            PromptBucket = "bucket"
        });
        var loader = new PromptLoader(s3, options);

        var job = new CanonicalJobRequest
        {
            PromptS3Key = string.Empty
        };

        Assert.That(
            async () => await loader.LoadPromptAsync(job, CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Prompt S3 key missing."));
    }

    [Test]
    public async Task LoadPromptAsync_ReturnsPromptText()
    {
        var s3 = Substitute.For<IAmazonS3>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            PromptBucket = "bucket"
        });
        var loader = new PromptLoader(s3, options);

        var content = "hello world";
        var response = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(content))
        };

        s3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var job = new CanonicalJobRequest
        {
            PromptS3Key = "prompts/job.txt"
        };

        var result = await loader.LoadPromptAsync(job, CancellationToken.None);

        Assert.That(result, Is.EqualTo(content));
    }
}
