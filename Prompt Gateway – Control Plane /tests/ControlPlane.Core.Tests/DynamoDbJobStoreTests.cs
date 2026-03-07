using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ControlPlane.Aws;
using ControlPlane.Core;
using NSubstitute;
using System.Text.Json;

namespace ControlPlane.Core.Tests;

public class DynamoDbJobStoreTests
{
    [Test]
    public async Task CreateAsync_WritesJobListIndexKeys()
    {
        var dynamoDb = Substitute.For<IAmazonDynamoDB>();
        var store = new DynamoDbJobStore(dynamoDb, new DynamoDbOptions { TableName = "test-table" });
        var now = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero);
        PutItemRequest? capturedRequest = null;
        dynamoDb
            .When(x => x.PutItemAsync(Arg.Any<PutItemRequest>(), Arg.Any<CancellationToken>()))
            .Do(callInfo => capturedRequest = callInfo.ArgAt<PutItemRequest>(0));

        var request = new CanonicalJobRequest
        {
            JobId = "job-index-create",
            AttemptId = "attempt-index-create",
            TraceId = "trace-index-create",
            TaskType = "chat_completion"
        };
        var job = JobRecord.Create(request, now);

        await store.CreateAsync(job, CancellationToken.None);

        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest!.Item["gsi1pk"].S, Is.EqualTo("JOBS"));
        Assert.That(capturedRequest.Item["gsi1sk"].S, Is.EqualTo(now.UtcDateTime.ToString("O")));
    }

    [Test]
    public async Task UpdateAsync_WritesJobListIndexKeys()
    {
        var dynamoDb = Substitute.For<IAmazonDynamoDB>();
        var store = new DynamoDbJobStore(dynamoDb, new DynamoDbOptions { TableName = "test-table" });
        var now = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero);
        var expectedUpdatedAt = now.AddMinutes(-1);
        UpdateItemRequest? capturedRequest = null;
        dynamoDb
            .When(x => x.UpdateItemAsync(Arg.Any<UpdateItemRequest>(), Arg.Any<CancellationToken>()))
            .Do(callInfo => capturedRequest = callInfo.ArgAt<UpdateItemRequest>(0));

        var request = new CanonicalJobRequest
        {
            JobId = "job-index-update",
            AttemptId = "attempt-index-update",
            TraceId = "trace-index-update",
            TaskType = "chat_completion"
        };
        var job = JobRecord.Create(request, expectedUpdatedAt);
        job.SetState(JobState.Routed, now);

        await store.UpdateAsync(job, expectedUpdatedAt, CancellationToken.None);

        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest!.UpdateExpression, Does.Contain("#jobListPk = :jobListPk"));
        Assert.That(capturedRequest.UpdateExpression, Does.Contain("#jobListSk = :jobListSk"));
        Assert.That(capturedRequest.ExpressionAttributeValues[":jobListPk"].S, Is.EqualTo("JOBS"));
        Assert.That(capturedRequest.ExpressionAttributeValues[":jobListSk"].S, Is.EqualTo(now.UtcDateTime.ToString("O")));
    }

    [Test]
    public async Task ListAsync_UsesJobListIndexQuery()
    {
        var dynamoDb = Substitute.For<IAmazonDynamoDB>();
        var store = new DynamoDbJobStore(dynamoDb, new DynamoDbOptions
        {
            TableName = "test-table",
            JobListIndexName = "gsi1"
        });
        var now = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero);

        var snapshot = new JobRecordSnapshot(
            "job-list-1",
            "trace-list-1",
            JobState.Created,
            now,
            now,
            "attempt-list-1",
            new CanonicalJobRequest
            {
                JobId = "job-list-1",
                AttemptId = "attempt-list-1",
                TraceId = "trace-list-1",
                TaskType = "chat_completion"
            },
            new List<JobAttemptSnapshot>
            {
                new("attempt-list-1", AttemptState.Created, null, null, null, now, now)
            });

        dynamoDb.QueryAsync(Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new QueryResponse
            {
                Items = new List<Dictionary<string, AttributeValue>>
                {
                    new()
                    {
                        ["pk"] = new() { S = "JOB#job-list-1" },
                        ["sk"] = new() { S = "JOB" },
                        ["job_snapshot"] = new() { S = SerializeSnapshot(snapshot) },
                        ["updated_at"] = new() { S = now.UtcDateTime.ToString("O") }
                    }
                },
                LastEvaluatedKey = new Dictionary<string, AttributeValue>()
            }));

        var jobs = await store.ListAsync(1, CancellationToken.None);

        Assert.That(jobs, Has.Count.EqualTo(1));
        Assert.That(jobs[0].JobId, Is.EqualTo("job-list-1"));
        await dynamoDb.Received(1).QueryAsync(
            Arg.Is<QueryRequest>(query =>
                query.IndexName == "gsi1"
                && query.KeyConditionExpression.Contains("#jobListPk = :jobListPk", StringComparison.Ordinal)
                && query.ScanIndexForward == false),
            Arg.Any<CancellationToken>());
        await dynamoDb.DidNotReceive().ScanAsync(Arg.Any<ScanRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateAndEnqueueDispatchAsync_WritesSingleTransaction()
    {
        var dynamoDb = Substitute.For<IAmazonDynamoDB>();
        var store = new DynamoDbJobStore(dynamoDb, new DynamoDbOptions
        {
            TableName = "test-table",
            JobListIndexName = "gsi1"
        });
        var previous = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero);
        var now = previous.AddMinutes(1);
        TransactWriteItemsRequest? capturedRequest = null;
        dynamoDb
            .When(x => x.TransactWriteItemsAsync(Arg.Any<TransactWriteItemsRequest>(), Arg.Any<CancellationToken>()))
            .Do(callInfo => capturedRequest = callInfo.ArgAt<TransactWriteItemsRequest>(0));

        var request = new CanonicalJobRequest
        {
            JobId = "job-transaction",
            AttemptId = "attempt-transaction",
            TraceId = "trace-transaction",
            TaskType = "chat_completion"
        };
        var job = JobRecord.Create(request, previous);
        job.SetState(JobState.Dispatched, now);
        var dispatch = new DispatchMessage
        {
            JobId = "job-transaction",
            AttemptId = "attempt-transaction",
            TraceId = "trace-transaction",
            Provider = "openai",
            Model = "gpt-4.1",
            IdempotencyKey = "job-transaction:attempt-transaction",
            Request = request
        };
        var outbox = new OutboxDispatchMessage("outbox-transaction", dispatch, now);
        var jobEvent = JobEvent.Dispatched("job-transaction", "attempt-transaction", now, dispatch);

        await store.UpdateAndEnqueueDispatchAsync(job, previous, outbox, jobEvent, CancellationToken.None);

        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest!.TransactItems, Has.Count.EqualTo(3));
        var update = capturedRequest.TransactItems[0].Update;
        var outboxPut = capturedRequest.TransactItems[1].Put;
        var eventPut = capturedRequest.TransactItems[2].Put;
        Assert.That(update, Is.Not.Null);
        Assert.That(update!.ConditionExpression, Does.Contain("#updatedAt = :expectedUpdatedAt"));
        Assert.That(update.ExpressionAttributeValues[":expectedUpdatedAt"].S, Is.EqualTo(previous.UtcDateTime.ToString("O")));
        Assert.That(outboxPut, Is.Not.Null);
        Assert.That(outboxPut!.Item["pk"].S, Is.EqualTo("OUTBOX"));
        Assert.That(outboxPut.Item["sk"].S, Is.EqualTo("outbox-transaction"));
        Assert.That(eventPut, Is.Not.Null);
        Assert.That(eventPut!.Item["pk"].S, Is.EqualTo("JOB#job-transaction"));
        Assert.That(eventPut.Item["sk"].S, Does.Contain("EVENT#"));
    }

    [Test]
    public async Task UpdateAsync_WithoutExpectedTimestamp_DoesNotUseConditionExpression()
    {
        var dynamoDb = Substitute.For<IAmazonDynamoDB>();
        var store = new DynamoDbJobStore(dynamoDb, new DynamoDbOptions { TableName = "test-table" });
        var createdAt = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero);
        var updatedAt = createdAt.AddMinutes(1);
        UpdateItemRequest? capturedRequest = null;
        dynamoDb
            .When(x => x.UpdateItemAsync(Arg.Any<UpdateItemRequest>(), Arg.Any<CancellationToken>()))
            .Do(callInfo => capturedRequest = callInfo.ArgAt<UpdateItemRequest>(0));

        var request = new CanonicalJobRequest
        {
            JobId = "job-0",
            AttemptId = "attempt-0",
            TraceId = "trace-0",
            TaskType = "chat_completion"
        };

        var job = JobRecord.Create(request, createdAt);
        job.SetState(JobState.Routed, updatedAt);

        await store.UpdateAsync(job, CancellationToken.None);

        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest!.ConditionExpression, Is.Null.Or.Empty);
        Assert.That(capturedRequest.ExpressionAttributeValues.ContainsKey(":expectedUpdatedAt"), Is.False);
    }

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

    [Test]
    public void UpdateAndEnqueueDispatchAsync_ThrowsOptimisticConcurrencyException_OnTransactionConflict()
    {
        var dynamoDb = Substitute.For<IAmazonDynamoDB>();
        var store = new DynamoDbJobStore(dynamoDb, new DynamoDbOptions { TableName = "test-table" });
        var previous = new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero);
        var now = previous.AddMinutes(1);
        var request = new CanonicalJobRequest
        {
            JobId = "job-transaction-conflict",
            AttemptId = "attempt-transaction-conflict",
            TraceId = "trace-transaction-conflict",
            TaskType = "chat_completion"
        };
        var job = JobRecord.Create(request, previous);
        job.SetState(JobState.Dispatched, now);
        var outbox = new OutboxDispatchMessage(
            "outbox-transaction-conflict",
            new DispatchMessage
            {
                JobId = request.JobId!,
                AttemptId = request.AttemptId!,
                TraceId = request.TraceId!,
                Provider = "openai",
                Model = "gpt-4.1",
                IdempotencyKey = "job-transaction-conflict:attempt-transaction-conflict",
                Request = request
            },
            now);

        dynamoDb.TransactWriteItemsAsync(Arg.Any<TransactWriteItemsRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TransactWriteItemsResponse>(
                new TransactionCanceledException("ConditionalCheckFailed")));

        var jobEvent = JobEvent.Dispatched(
            "job-transaction-conflict",
            "attempt-transaction-conflict",
            now,
            outbox.Message);

        Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            store.UpdateAndEnqueueDispatchAsync(job, previous, outbox, jobEvent, CancellationToken.None));
    }

    private static string SerializeSnapshot(JobRecordSnapshot snapshot)
    {
        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
