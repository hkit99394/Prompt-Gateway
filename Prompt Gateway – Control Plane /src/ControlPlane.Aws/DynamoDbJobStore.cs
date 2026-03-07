using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ControlPlane.Core;

namespace ControlPlane.Aws;

public sealed class DynamoDbJobStore : DynamoDbStoreBase, IJobStore, ITransactionalJobStore
{
    private const string PartitionKey = "pk";
    private const string SortKey = "sk";
    private const string SnapshotField = "job_snapshot";
    private const string UpdatedAtField = "updated_at";
    private const string JobListPartitionKeyField = "gsi1pk";
    private const string JobListSortKeyField = "gsi1sk";
    private const string JobListPartition = "JOBS";
    private const string OutboxPartition = "OUTBOX";
    private const string OutboxStatusField = "status";
    private const string OutboxMessageField = "message_json";
    private const string OutboxCreatedAtField = "created_at";
    private const string EventField = "event_json";
    private const string EventPrefix = "EVENT#";
    private const string ResponseField = "response_json";
    private const string TtlField = "ttl";
    private const string DedupePartitionPrefix = "DEDUP#";
    private const string DedupeSortPrefix = "ATTEMPT#";
    private const string DedupeStatusField = "status";
    private const string JobPartitionPrefix = "JOB#";
    private const string JobSortKey = "JOB";

    public DynamoDbJobStore(IAmazonDynamoDB dynamoDb, DynamoDbOptions options)
        : base(dynamoDb, options)
    {
    }

    public async Task CreateAsync(JobRecord job, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var snapshot = ToSnapshot(job);
        var request = new PutItemRequest
        {
            TableName = Options.TableName,
            Item = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr($"{JobPartitionPrefix}{job.JobId}"),
                [SortKey] = Attr(JobSortKey),
                [SnapshotField] = Attr(JsonSerializer.Serialize(snapshot, SerializerOptions)),
                [UpdatedAtField] = Attr(FormatTimestamp(job.UpdatedAt)),
                [JobListPartitionKeyField] = Attr(JobListPartition),
                [JobListSortKeyField] = Attr(FormatTimestamp(job.UpdatedAt))
            },
            ConditionExpression = "attribute_not_exists(#pk)",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#pk"] = PartitionKey
            }
        };

        await DynamoDb.PutItemAsync(request, cancellationToken);
    }

    public async Task<JobRecord?> GetAsync(string jobId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var response = await DynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = Options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr($"{JobPartitionPrefix}{jobId}"),
                [SortKey] = Attr(JobSortKey)
            }
        }, cancellationToken);

        if (response.Item is null || response.Item.Count == 0)
        {
            return null;
        }

        var snapshotJson = GetString(response.Item, SnapshotField);
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return null;
        }

        var snapshot = JsonSerializer.Deserialize<JobRecordSnapshot>(snapshotJson, SerializerOptions);
        return snapshot is null ? null : JobRecord.Restore(snapshot);
    }

    public async Task UpdateAsync(JobRecord job, CancellationToken cancellationToken)
    {
        await UpdateInternalAsync(job, expectedUpdatedAt: null, cancellationToken);
    }

    public async Task UpdateAsync(
        JobRecord job,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken)
    {
        await UpdateInternalAsync(job, expectedUpdatedAt, cancellationToken);
    }

    private async Task UpdateInternalAsync(
        JobRecord job,
        DateTimeOffset? expectedUpdatedAt,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var snapshot = ToSnapshot(job);
        var request = new UpdateItemRequest
        {
            TableName = Options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr($"{JobPartitionPrefix}{job.JobId}"),
                [SortKey] = Attr(JobSortKey)
            },
            UpdateExpression = "SET #snapshot = :snapshot, #updatedAt = :updatedAt, #jobListPk = :jobListPk, #jobListSk = :jobListSk",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#snapshot"] = SnapshotField,
                ["#updatedAt"] = UpdatedAtField,
                ["#jobListPk"] = JobListPartitionKeyField,
                ["#jobListSk"] = JobListSortKeyField
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":snapshot"] = Attr(JsonSerializer.Serialize(snapshot, SerializerOptions)),
                [":updatedAt"] = Attr(FormatTimestamp(job.UpdatedAt)),
                [":jobListPk"] = Attr(JobListPartition),
                [":jobListSk"] = Attr(FormatTimestamp(job.UpdatedAt))
            }
        };

        if (expectedUpdatedAt.HasValue)
        {
            request.ConditionExpression = "(#updatedAt = :expectedUpdatedAt) OR attribute_not_exists(#updatedAt)";
            request.ExpressionAttributeValues[":expectedUpdatedAt"] = Attr(FormatTimestamp(expectedUpdatedAt.Value));
        }

        try
        {
            await DynamoDb.UpdateItemAsync(request, cancellationToken);
        }
        catch (ConditionalCheckFailedException ex)
        {
            if (expectedUpdatedAt.HasValue)
            {
                throw new OptimisticConcurrencyException(
                    $"Job '{job.JobId}' update conflict detected. Reload and retry.",
                    ex);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<JobSummary>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (limit <= 0)
        {
            return Array.Empty<JobSummary>();
        }

        var results = new List<JobSummary>(limit);
        Dictionary<string, AttributeValue>? startKey = null;

        while (results.Count < limit)
        {
            var pageLimit = Math.Min(100, limit - results.Count);
            var request = new QueryRequest
            {
                TableName = Options.TableName,
                IndexName = Options.JobListIndexName,
                KeyConditionExpression = "#jobListPk = :jobListPk",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#jobListPk"] = JobListPartitionKeyField
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":jobListPk"] = Attr(JobListPartition)
                },
                Limit = pageLimit,
                ExclusiveStartKey = startKey,
                ScanIndexForward = false,
                ProjectionExpression = "#pk, #sk, #snapshot, #updatedAt"
            };

            request.ExpressionAttributeNames["#pk"] = PartitionKey;
            request.ExpressionAttributeNames["#sk"] = SortKey;
            request.ExpressionAttributeNames["#snapshot"] = SnapshotField;
            request.ExpressionAttributeNames["#updatedAt"] = UpdatedAtField;

            var response = await DynamoDb.QueryAsync(request, cancellationToken);
            foreach (var item in response.Items)
            {
                var snapshotJson = GetString(item, SnapshotField);
                if (string.IsNullOrWhiteSpace(snapshotJson))
                {
                    continue;
                }

                var snapshot = JsonSerializer.Deserialize<JobRecordSnapshot>(snapshotJson, SerializerOptions);
                if (snapshot is null)
                {
                    continue;
                }

                results.Add(new JobSummary(
                    snapshot.JobId,
                    snapshot.TraceId,
                    snapshot.CurrentAttemptId,
                    snapshot.State,
                    snapshot.CreatedAt,
                    snapshot.UpdatedAt));
            }

            startKey = response.LastEvaluatedKey;
            if (startKey is null || startKey.Count == 0)
            {
                break;
            }
        }

        return results;
    }

    public async Task UpdateAndEnqueueDispatchAsync(
        JobRecord job,
        DateTimeOffset expectedUpdatedAt,
        OutboxDispatchMessage message,
        JobEvent jobEvent,
        string? dedupeAttemptId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var snapshot = ToSnapshot(job);
        var updatedAt = FormatTimestamp(job.UpdatedAt);
        var expected = FormatTimestamp(expectedUpdatedAt);
        var outboxCreatedAt = FormatTimestamp(message.CreatedAt);
        var serializedSnapshot = JsonSerializer.Serialize(snapshot, SerializerOptions);
        var serializedOutbox = JsonSerializer.Serialize(message, SerializerOptions);
        var eventSortKey = $"{EventPrefix}{FormatTimestamp(jobEvent.OccurredAt)}#{jobEvent.AttemptId}#{jobEvent.Type}";
        var serializedEvent = JsonSerializer.Serialize(jobEvent, SerializerOptions);
        var transactItems = new List<TransactWriteItem>
        {
            new()
            {
                Update = new Update
                {
                    TableName = Options.TableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        [PartitionKey] = Attr($"{JobPartitionPrefix}{job.JobId}"),
                        [SortKey] = Attr(JobSortKey)
                    },
                    UpdateExpression = "SET #snapshot = :snapshot, #updatedAt = :updatedAt, #jobListPk = :jobListPk, #jobListSk = :jobListSk",
                    ConditionExpression = "(#updatedAt = :expectedUpdatedAt) OR attribute_not_exists(#updatedAt)",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#snapshot"] = SnapshotField,
                        ["#updatedAt"] = UpdatedAtField,
                        ["#jobListPk"] = JobListPartitionKeyField,
                        ["#jobListSk"] = JobListSortKeyField
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":snapshot"] = Attr(serializedSnapshot),
                        [":updatedAt"] = Attr(updatedAt),
                        [":expectedUpdatedAt"] = Attr(expected),
                        [":jobListPk"] = Attr(JobListPartition),
                        [":jobListSk"] = Attr(updatedAt)
                    }
                }
            },
            new()
            {
                Put = new Put
                {
                    TableName = Options.TableName,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        [PartitionKey] = Attr(OutboxPartition),
                        [SortKey] = Attr(message.OutboxId),
                        [OutboxStatusField] = Attr("pending"),
                        [OutboxCreatedAtField] = Attr(outboxCreatedAt),
                        [OutboxMessageField] = Attr(serializedOutbox)
                    },
                    ConditionExpression = "attribute_not_exists(#pk) AND attribute_not_exists(#sk)",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#pk"] = PartitionKey,
                        ["#sk"] = SortKey
                    }
                }
            },
            new()
            {
                Put = new Put
                {
                    TableName = Options.TableName,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        [PartitionKey] = Attr($"{JobPartitionPrefix}{jobEvent.JobId}"),
                        [SortKey] = Attr(eventSortKey),
                        [EventField] = Attr(serializedEvent)
                    }
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(dedupeAttemptId))
        {
            transactItems.Add(new TransactWriteItem
            {
                Update = BuildDedupeCompletedUpdate(job.JobId, dedupeAttemptId!)
            });
        }

        if (Options.EventTtlDays > 0)
        {
            transactItems[2].Put!.Item[TtlField] = Attr(ToUnixTimeSeconds(jobEvent.OccurredAt.AddDays(Options.EventTtlDays)));
        }

        var request = new TransactWriteItemsRequest { TransactItems = transactItems };

        try
        {
            await DynamoDb.TransactWriteItemsAsync(request, cancellationToken);
        }
        catch (TransactionCanceledException ex)
        {
            var jobUpdateConflict = ex.CancellationReasons is { Count: > 0 }
                ? string.Equals(ex.CancellationReasons[0]?.Code, "ConditionalCheckFailed", StringComparison.Ordinal)
                : ex.Message.Contains("ConditionalCheckFailed", StringComparison.Ordinal);

            if (jobUpdateConflict)
            {
                throw new OptimisticConcurrencyException(
                    $"Job '{job.JobId}' update conflict detected. Reload and retry.",
                    ex);
            }

            throw;
        }
    }

    public async Task FinalizeResultIngestionAsync(
        JobRecord job,
        DateTimeOffset expectedUpdatedAt,
        string dedupeAttemptId,
        CanonicalResponse response,
        JobEvent? jobEvent,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var snapshot = ToSnapshot(job);
        var updatedAt = FormatTimestamp(job.UpdatedAt);
        var expected = FormatTimestamp(expectedUpdatedAt);
        var serializedSnapshot = JsonSerializer.Serialize(snapshot, SerializerOptions);
        var serializedResponse = JsonSerializer.Serialize(response, SerializerOptions);

        var transactItems = new List<TransactWriteItem>
        {
            new()
            {
                Update = new Update
                {
                    TableName = Options.TableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        [PartitionKey] = Attr($"{JobPartitionPrefix}{job.JobId}"),
                        [SortKey] = Attr(JobSortKey)
                    },
                    UpdateExpression = "SET #snapshot = :snapshot, #updatedAt = :updatedAt, #jobListPk = :jobListPk, #jobListSk = :jobListSk",
                    ConditionExpression = "(#updatedAt = :expectedUpdatedAt) OR attribute_not_exists(#updatedAt)",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#snapshot"] = SnapshotField,
                        ["#updatedAt"] = UpdatedAtField,
                        ["#jobListPk"] = JobListPartitionKeyField,
                        ["#jobListSk"] = JobListSortKeyField
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":snapshot"] = Attr(serializedSnapshot),
                        [":updatedAt"] = Attr(updatedAt),
                        [":expectedUpdatedAt"] = Attr(expected),
                        [":jobListPk"] = Attr(JobListPartition),
                        [":jobListSk"] = Attr(updatedAt)
                    }
                }
            },
            new()
            {
                Put = new Put
                {
                    TableName = Options.TableName,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        [PartitionKey] = Attr($"{JobPartitionPrefix}{job.JobId}"),
                        [SortKey] = Attr($"RESULT#ATTEMPT#{dedupeAttemptId}"),
                        [ResponseField] = Attr(serializedResponse)
                    }
                }
            },
            new()
            {
                Put = new Put
                {
                    TableName = Options.TableName,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        [PartitionKey] = Attr($"{JobPartitionPrefix}{job.JobId}"),
                        [SortKey] = Attr("RESULT#FINAL"),
                        [ResponseField] = Attr(serializedResponse)
                    }
                }
            },
            new()
            {
                Update = BuildDedupeCompletedUpdate(job.JobId, dedupeAttemptId)
            }
        };

        if (Options.ResultTtlDays > 0)
        {
            var resultTtl = Attr(ToUnixTimeSeconds(job.UpdatedAt.AddDays(Options.ResultTtlDays)));
            transactItems[1].Put!.Item[TtlField] = resultTtl;
            transactItems[2].Put!.Item[TtlField] = resultTtl;
        }

        if (jobEvent is not null)
        {
            var eventSortKey = $"{EventPrefix}{FormatTimestamp(jobEvent.OccurredAt)}#{jobEvent.AttemptId}#{jobEvent.Type}";
            transactItems.Add(new TransactWriteItem
            {
                Put = new Put
                {
                    TableName = Options.TableName,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        [PartitionKey] = Attr($"{JobPartitionPrefix}{jobEvent.JobId}"),
                        [SortKey] = Attr(eventSortKey),
                        [EventField] = Attr(JsonSerializer.Serialize(jobEvent, SerializerOptions))
                    }
                }
            });

            if (Options.EventTtlDays > 0)
            {
                transactItems[^1].Put!.Item[TtlField] = Attr(ToUnixTimeSeconds(jobEvent.OccurredAt.AddDays(Options.EventTtlDays)));
            }
        }

        var request = new TransactWriteItemsRequest { TransactItems = transactItems };

        try
        {
            await DynamoDb.TransactWriteItemsAsync(request, cancellationToken);
        }
        catch (TransactionCanceledException ex)
        {
            var jobUpdateConflict = ex.CancellationReasons is { Count: > 0 }
                ? string.Equals(ex.CancellationReasons[0]?.Code, "ConditionalCheckFailed", StringComparison.Ordinal)
                : ex.Message.Contains("ConditionalCheckFailed", StringComparison.Ordinal);

            if (jobUpdateConflict)
            {
                throw new OptimisticConcurrencyException(
                    $"Job '{job.JobId}' update conflict detected. Reload and retry.",
                    ex);
            }

            throw;
        }
    }

    private Update BuildDedupeCompletedUpdate(string jobId, string dedupeAttemptId)
    {
        return new Update
        {
            TableName = Options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [PartitionKey] = Attr($"{DedupePartitionPrefix}{jobId}"),
                [SortKey] = Attr($"{DedupeSortPrefix}{dedupeAttemptId}")
            },
            UpdateExpression = "SET #status = :completed",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = DedupeStatusField
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":completed"] = Attr("completed")
            }
        };
    }

    private static JobRecordSnapshot ToSnapshot(JobRecord job)
    {
        var attempts = job.Attempts.Select(attempt => new JobAttemptSnapshot(
            attempt.AttemptId,
            attempt.State,
            attempt.Provider,
            attempt.Model,
            attempt.RoutingDecision,
            attempt.CreatedAt,
            attempt.UpdatedAt)).ToList();

        return new JobRecordSnapshot(
            job.JobId,
            job.TraceId,
            job.State,
            job.CreatedAt,
            job.UpdatedAt,
            job.CurrentAttemptId,
            job.Request,
            attempts);
    }
}
