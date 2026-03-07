using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ControlPlane.Aws;
using ControlPlane.Core;
using NSubstitute;

namespace ControlPlane.Core.Tests;

public class DynamoDbJobStoreTests
{
    [Test]
    public async Task UpdateAsync_UsesExpectedUpdatedAtCondition()
    {
        var dynamoDb = Substitute.For<IAmazonDynamoDB>();
        var store = new DynamoDbJobStore(dynamoDb, new DynamoDbOptions { TableName = "test-table" });
        var now = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero);
        var expectedUpdatedAt = now.AddMinutes(-1);

        var request = new CanonicalJobRequest
        {
            JobId = "job-1",
            AttemptId = "attempt-1",
            TraceId = "trace-1",
            TaskType = "chat_completion"
        };

        var job = JobRecord.Create(request, expectedUpdatedAt);
        job.SetState(JobState.Routed, now);

        await store.UpdateAsync(job, expectedUpdatedAt, CancellationToken.None);

        await dynamoDb.Received(1).UpdateItemAsync(
            Arg.Is<UpdateItemRequest>(update =>
                update.ConditionExpression != null &&
                update.ConditionExpression.Contains("#updatedAt = :expectedUpdatedAt", StringComparison.Ordinal) &&
                update.ExpressionAttributeValues.ContainsKey(":expectedUpdatedAt")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void UpdateAsync_ThrowsOptimisticConcurrencyException_OnConditionalFailure()
    {
        var dynamoDb = Substitute.For<IAmazonDynamoDB>();
        var store = new DynamoDbJobStore(dynamoDb, new DynamoDbOptions { TableName = "test-table" });
        var now = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero);
        var expectedUpdatedAt = now.AddMinutes(-1);

        var request = new CanonicalJobRequest
        {
            JobId = "job-2",
            AttemptId = "attempt-2",
            TraceId = "trace-2",
            TaskType = "chat_completion"
        };

        var job = JobRecord.Create(request, expectedUpdatedAt);
        job.SetState(JobState.Routed, now);

        dynamoDb.UpdateItemAsync(Arg.Any<UpdateItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<UpdateItemResponse>(new ConditionalCheckFailedException("stale")));

        Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            store.UpdateAsync(job, expectedUpdatedAt, CancellationToken.None));
    }
}
