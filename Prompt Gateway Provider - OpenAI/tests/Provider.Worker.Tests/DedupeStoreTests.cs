using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Provider.Worker.Aws;
using Provider.Worker.Options;
using Provider.Worker.Services;

namespace Provider.Worker.Tests;

public class DedupeStoreTests
{
    [Test]
    public async Task TryStartAsync_UsesMemoryStoreWhenNoTableConfigured()
    {
        var logger = Substitute.For<ILogger<DedupeStore>>();
        var dynamo = Substitute.For<IAmazonDynamoDB>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            DedupeTableName = string.Empty
        });
        var store = new DedupeStore(logger, dynamo, options);

        var first = await store.TryStartAsync("job-1", "attempt-1", CancellationToken.None);
        var second = await store.TryStartAsync("job-1", "attempt-1", CancellationToken.None);

        Assert.That(first, Is.EqualTo(DedupeDecision.Started));
        Assert.That(second, Is.EqualTo(DedupeDecision.DuplicateInProgress));
    }

    [Test]
    public async Task TryStartAsync_ReturnsDuplicateCompletedOnConditionalCheckFailure()
    {
        var logger = Substitute.For<ILogger<DedupeStore>>();
        var dynamo = Substitute.For<IAmazonDynamoDB>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            DedupeTableName = "dedupe"
        });
        var store = new DedupeStore(logger, dynamo, options);

        dynamo.PutItemAsync(Arg.Any<PutItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<PutItemResponse>(
                new ConditionalCheckFailedException("exists")));
        dynamo.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["status"] = new AttributeValue { S = "completed" }
                }
            });

        var result = await store.TryStartAsync("job-2", "attempt-2", CancellationToken.None);

        Assert.That(result, Is.EqualTo(DedupeDecision.DuplicateCompleted));
    }

    [Test]
    public async Task TryStartAsync_FallsBackToMemoryOnDynamoFailure()
    {
        var logger = Substitute.For<ILogger<DedupeStore>>();
        var dynamo = Substitute.For<IAmazonDynamoDB>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            DedupeTableName = "dedupe"
        });
        var store = new DedupeStore(logger, dynamo, options);

        dynamo.PutItemAsync(Arg.Any<PutItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<PutItemResponse>(new Exception("boom")));

        var first = await store.TryStartAsync("job-3", "attempt-3", CancellationToken.None);
        var second = await store.TryStartAsync("job-3", "attempt-3", CancellationToken.None);

        Assert.That(first, Is.EqualTo(DedupeDecision.Started));
        Assert.That(second, Is.EqualTo(DedupeDecision.DuplicateInProgress));
    }

    [Test]
    public async Task MarkCompletedAsync_DoesNotThrowOnDynamoFailure()
    {
        var logger = Substitute.For<ILogger<DedupeStore>>();
        var dynamo = Substitute.For<IAmazonDynamoDB>();
        var options = TestOptions.Create(new ProviderWorkerOptions
        {
            DedupeTableName = "dedupe"
        });
        var store = new DedupeStore(logger, dynamo, options);

        dynamo.UpdateItemAsync(Arg.Any<UpdateItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<UpdateItemResponse>(new Exception("boom")));

        Assert.DoesNotThrowAsync(async () =>
            await store.MarkCompletedAsync("job-4", "attempt-4", CancellationToken.None));
    }
}
