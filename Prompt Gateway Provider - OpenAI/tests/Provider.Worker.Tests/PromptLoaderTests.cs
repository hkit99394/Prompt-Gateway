using NSubstitute;
using Provider.Worker.Models;
using Provider.Worker.Options;
using Provider.Worker.Services;

namespace Provider.Worker.Tests;

public class PromptTemplateStoreTests
{
    [Test]
    public void GetTemplateAsync_ThrowsWhenBucketMissing()
    {
        var store = Substitute.For<IObjectStore>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            PromptBucket = string.Empty
        });
        var templateStore = new PromptTemplateStore(store, options);

        var job = new CanonicalJobRequest
        {
            PromptS3Key = "prompts/job.txt"
        };

        Assert.That(
            async () => await templateStore.GetTemplateAsync(job, CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Prompt bucket not configured."));
    }

    [Test]
    public void GetTemplateAsync_ThrowsWhenKeyMissing()
    {
        var store = Substitute.For<IObjectStore>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            PromptBucket = "bucket"
        });
        var templateStore = new PromptTemplateStore(store, options);

        var job = new CanonicalJobRequest
        {
            PromptS3Key = string.Empty
        };

        Assert.That(
            async () => await templateStore.GetTemplateAsync(job, CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Prompt key missing."));
    }

    [Test]
    public async Task GetTemplateAsync_ReturnsPromptText()
    {
        var store = Substitute.For<IObjectStore>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            PromptBucket = "bucket"
        });
        var templateStore = new PromptTemplateStore(store, options);

        var job = new CanonicalJobRequest
        {
            PromptS3Key = "prompts/job.txt"
        };

        store.GetObjectTextAsync("bucket", "prompts/job.txt", Arg.Any<CancellationToken>())
            .Returns("hello world");

        var result = await templateStore.GetTemplateAsync(job, CancellationToken.None);

        Assert.That(result, Is.EqualTo("hello world"));
    }

    [Test]
    public void GetTemplateAsync_ThrowsWhenBucketOverrideIsNotAllowed()
    {
        var store = Substitute.For<IObjectStore>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            PromptBucket = "bucket-a"
        });
        var templateStore = new PromptTemplateStore(store, options);

        var job = new CanonicalJobRequest
        {
            PromptBucket = "bucket-b",
            PromptS3Key = "prompts/job.txt"
        };

        Assert.That(
            async () => await templateStore.GetTemplateAsync(job, CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Prompt bucket override is not allowed."));
    }

    [Test]
    public void GetTemplateAsync_ThrowsWhenPromptKeyIsInvalid()
    {
        var store = Substitute.For<IObjectStore>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            PromptBucket = "bucket"
        });
        var templateStore = new PromptTemplateStore(store, options);

        var job = new CanonicalJobRequest
        {
            PromptS3Key = "../secrets.txt"
        };

        Assert.That(
            async () => await templateStore.GetTemplateAsync(job, CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Invalid prompt key."));
    }
}
