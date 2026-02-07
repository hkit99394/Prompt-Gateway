using Amazon.S3;
using Amazon.S3.Model;
using NSubstitute;
using Provider.Worker.Models;
using Provider.Worker.Options;
using Provider.Worker.Services;

namespace Provider.Worker.Tests;

public class ResultPayloadStoreTests
{
    [Test]
    public async Task StoreIfLargeAsync_ReturnsNullForSmallPayload()
    {
        var s3 = Substitute.For<IAmazonS3>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            LargePayloadThresholdBytes = 100,
            ResultBucket = "results"
        });
        var store = new ResultPayloadStore(s3, options);

        var job = new CanonicalJobRequest { JobId = "job", AttemptId = "attempt" };
        var result = await store.StoreIfLargeAsync(job, "small", CancellationToken.None);

        Assert.That(result, Is.Null);
        await s3.DidNotReceive()
            .PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StoreIfLargeAsync_UploadsLargePayload()
    {
        var s3 = Substitute.For<IAmazonS3>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            LargePayloadThresholdBytes = 1,
            ResultBucket = "results",
            ResultPrefix = "results/"
        });
        var store = new ResultPayloadStore(s3, options);

        var job = new CanonicalJobRequest { JobId = "job-1", AttemptId = "attempt-1" };
        var result = await store.StoreIfLargeAsync(job, "large payload", CancellationToken.None);

        Assert.That(result, Is.EqualTo("s3://results/results/job-1/attempt-1/payload.json"));
        await s3.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(req =>
                req.BucketName == "results" &&
                req.Key == "results/job-1/attempt-1/payload.json"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StoreAsync_UploadsPayloadWithFileName()
    {
        var s3 = Substitute.For<IAmazonS3>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            ResultBucket = "results",
            ResultPrefix = "results/"
        });
        var store = new ResultPayloadStore(s3, options);

        var job = new CanonicalJobRequest { JobId = "job-2", AttemptId = "attempt-2" };
        var result = await store.StoreAsync(job, "payload", "error.json", CancellationToken.None);

        Assert.That(result, Is.EqualTo("s3://results/results/job-2/attempt-2/error.json"));
        await s3.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(req =>
                req.BucketName == "results" &&
                req.Key == "results/job-2/attempt-2/error.json"),
            Arg.Any<CancellationToken>());
    }
}
