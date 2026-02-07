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
        var store = Substitute.For<IObjectStore>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            LargePayloadThresholdBytes = 100,
            ResultBucket = "results"
        });
        var payloadStore = new ResultPayloadStore(store, options);

        var job = new CanonicalJobRequest { JobId = "job", AttemptId = "attempt" };
        var result = await payloadStore.StoreIfLargeAsync(job, "small", CancellationToken.None);

        Assert.That(result, Is.Null);
        await store.DidNotReceive()
            .PutObjectTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StoreIfLargeAsync_UploadsLargePayload()
    {
        var store = Substitute.For<IObjectStore>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            LargePayloadThresholdBytes = 1,
            ResultBucket = "results",
            ResultPrefix = "results/"
        });
        var payloadStore = new ResultPayloadStore(store, options);

        var job = new CanonicalJobRequest { JobId = "job-1", AttemptId = "attempt-1" };
        store.PutObjectTextAsync(
                "results",
                "results/job-1/attempt-1/payload.json",
                Arg.Any<string>(),
                "application/json",
                Arg.Any<CancellationToken>())
            .Returns("s3://results/results/job-1/attempt-1/payload.json");

        var result = await payloadStore.StoreIfLargeAsync(job, "large payload", CancellationToken.None);

        Assert.That(result, Is.EqualTo("s3://results/results/job-1/attempt-1/payload.json"));
        await store.Received(1).PutObjectTextAsync(
            "results",
            "results/job-1/attempt-1/payload.json",
            Arg.Any<string>(),
            "application/json",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StoreAsync_UploadsPayloadWithFileName()
    {
        var store = Substitute.For<IObjectStore>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            ResultBucket = "results",
            ResultPrefix = "results/"
        });
        var payloadStore = new ResultPayloadStore(store, options);

        var job = new CanonicalJobRequest { JobId = "job-2", AttemptId = "attempt-2" };
        store.PutObjectTextAsync(
                "results",
                "results/job-2/attempt-2/error.json",
                Arg.Any<string>(),
                "application/json",
                Arg.Any<CancellationToken>())
            .Returns("s3://results/results/job-2/attempt-2/error.json");

        var result = await payloadStore.StoreAsync(job, "payload", "error.json", CancellationToken.None);

        Assert.That(result, Is.EqualTo("s3://results/results/job-2/attempt-2/error.json"));
        await store.Received(1).PutObjectTextAsync(
            "results",
            "results/job-2/attempt-2/error.json",
            Arg.Any<string>(),
            "application/json",
            Arg.Any<CancellationToken>());
    }
}
